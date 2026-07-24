# V9. Фикс багов фильтра по значению (диалог + источник данных)

Исправить 6 багов, найденных при обкатке V4/V6/V7. Правки точечные, файлы:
`Components/Grid/ClayGridPageBase.cs` (метод `LoadDistinctValuesAsync`) и
`Components/Grid/ClayColumnValueFilterDialog.razor` (`@code` внутри).

Проект слегка изменился — перед правкой открыть актуальные версии этих файлов и
свериться с номерами строк ниже (могли сдвинуться).

---

## Баг 1 — подписи вида `{DapperRow, v = 'COVID-19'}` (и корень багов 3, 5, 6)
**Причина.** В `LoadDistinctValuesAsync` значения читаются как
`(await Db.QueryAsync<object>(valuesSql, dp)).ToList()`. Dapper на `object`
возвращает `DapperRow` (всю строку), а не скаляр. В список попадают `DapperRow`:
- в UI `FormatValue` → `_descriptor.Format(row)` → `row.ToString()` →
  `{DapperRow, v = '…'}`;
- сравнение в `HashSet<object?>` идёт по ссылке, а не по значению;
- в `vf.Values` уходят `DapperRow` → SQL-параметризация `IN(...)` ломается
  (фильтр «не применяется»).

**Фикс.** В `LoadDistinctValuesAsync` извлекать скаляр из поля `v` (алиас в SQL):
```csharp
// было:
// var rawValues = (await Db.QueryAsync<object>(valuesSql, dp)).ToList();

// стало:
var rows = await Db.QueryAsync(valuesSql, dp); // dynamic → DapperRow (IDictionary<string,object>)
var rawValues = rows
    .Select(r => (object?)((IDictionary<string, object>)r)["v"])
    .Select(v => v is DBNull ? null : v)
    .ToList();
```
Дальше `Values = rawValues.AsReadOnly()` — уже скаляры (string/int/DateTime/…),
`_descriptor.Format` их отформатирует корректно, сравнение по значению заработает.

---

## Баг 2 — кнопка «OK» → «Применить»
В `ClayColumnValueFilterDialog.razor`, `<DialogActions>`: текст кнопки применения
заменить `OK` на `Применить`.

---

## Баг 4 — по умолчанию должны быть выделены все элементы
**Причина.** `_checkedValues` стартует пустым; галочки восстанавливаются только из
`ExistingValueFilter`. Когда фильтра ещё нет — ничего не отмечено.

**Фикс.** В `OnInitializedAsync`, после загрузки `_result`, если фильтра нет —
отметить все значения и пустышки. В блоке восстановления сделать ветку else:
```csharp
if (ExistingValueFilter is not null && _result is not null)
{
    // …существующее восстановление галочек…
}
else if (_result is not null)
{
    // по умолчанию выбраны все (Excel-поведение)
    foreach (var v in _result.Values) _checkedValues.Add(v);
    _blankChecked = _result.HasBlanks;
}
```
Тогда при открытии «Выделить все» = checked, а нажатие «Применить» без изменений
корректно вернёт `ClearedSentinel` (фильтра нет).

---

## Баги 5, 6 — «Выделить все» не работает
**Причина.** Чекбокс «Выделить все» сделан tri-state, а пользовательский клик по
tri-state отдаёт неоднозначное следующее состояние (в т.ч. `null`), из-за чего
`OnSelectAllChanged(null)` уходит в ветку «снять всё». Плюс список не
перерисовывается синхронно.

**Фикс.** Вести выбор явной логикой «всё/ничего», не доверяя переданному состоянию.
Разметку select-all заменить на двухсостоянийную, а обработчик — на toggle по
текущему состоянию (индикатор «частично» оставить только для вида).

Разметка (`value-filter-select-all`):
```razor
<MudCheckBox T="bool"
             Checked="@(IsSelectAllState == true)"
             CheckedChanged="OnSelectAllToggle"
             Label="(Выделить все)"
             Dense="true"
             Style="font-weight:bold" />
```
Обработчик (заменить `OnSelectAllChanged`):
```csharp
private void OnSelectAllToggle(bool _)
{
    if (_result is null) return;
    var allSelected = IsSelectAllState == true;
    if (allSelected)                    // сейчас всё → снять всё
    {
        _checkedValues.Clear();
        _blankChecked = false;
    }
    else                                // иначе → выбрать всё
    {
        _checkedValues = _result.Values.ToHashSet();
        _blankChecked = _result.HasBlanks;
    }
    StateHasChanged();
}
```
`IsSelectAllState` (bool? для вычисления состояния) оставить как есть — он теперь
используется только для чтения (`== true`). В `OnValueCheckedChanged` добавить
`StateHasChanged();` в конце, чтобы select-all и список сразу отражали изменения.

> Если нужен визуальный индикатор «частично» (indeterminate) — оставить
> `T="bool?"`, `Value="@IsSelectAllState"`, `TriState="true"`,
> `ValueChanged="OnSelectAllToggle"` и в `OnSelectAllToggle(bool? _)` игнорировать
> переданное значение так же, как выше (решение по `IsSelectAllState`).

---

## Баг 3 — выбранный фильтр не применяется
Отдельной правки в маршрутизации не требуется: `OpenValueFilterDialog` →
`ApplyValueFilter` → вставка в `_filterRoot` + `NotifyQueryChanged` уже корректны.
Баг устраняется фиксом 1 (скалярные значения вместо `DapperRow`, из-за которых
`IN(...)` не строился) и фиксом 4 (адекватное стартовое состояние выбора).
После правок проверить сценарий: снять 1–3 значения из полного набора → «Применить»
→ грид фильтруется (в SQL — `NOT IN(снятые)` по инверсии, треб. 14); снять большую
часть → `IN(выбранные)`.

---

## Критерии
- [ ] Значения в списке отображаются как сами данные («COVID-19»), не `{DapperRow…}`.
- [ ] Кнопка применения подписана «Применить».
- [ ] При открытии диалога (без активного фильтра) отмечены все значения и «(пустые)».
- [ ] Клик «Выделить все» снимает/ставит все галочки; состояние сразу видно в списке.
- [ ] Выбор подмножества значений реально фильтрует грид (IN / NOT IN по инверсии).
- [ ] `dotnet build` без ошибок.
