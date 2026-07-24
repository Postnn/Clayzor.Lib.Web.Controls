# V12. Фильтр по значению как третий вид чипа + фикс пустого чипа

Сейчас на колонку есть два варианта фильтра: (а) чип по колонке через
перетаскивание/⋮ (лист `ColumnFilter` с `Source=ColumnDialog`) и (б)
настраиваемый (составной) фильтр. Добавить третий вид — **по значению**
(`ValueFilter`) — как отдельный чип, с корректной подписью и взаимоисключением с
вариантом (а).

Файлы: `Components/Grid/ClayGrid.Filtering.cs`, `Components/Grid/ClayGrid.razor`.

## Баг: рендерится пустой чип
**Причина.** `HasComposite` считает `ValueFilter` составным узлом:
```csharp
_filterRoot.Nodes.Any(n => n is not ColumnFilter cf || cf.Source != ClayFilterSource.ColumnDialog);
```
Как только добавлен `ValueFilter`, `HasComposite == true`, трей уходит в ветку
составного фильтра и рисует один чип с `BuildFilterDescription()`; описатель
`ValueFilter` не знает → текст пустой → **пустой чип**.

**Фикс.** `ValueFilter` — не составной. В `ClayGrid.Filtering.cs`:
```csharp
private bool HasComposite =>
    _filterRoot.Nodes.Any(n =>
        n is not ValueFilter
        && (n is not ColumnFilter cf || cf.Source != ClayFilterSource.ColumnDialog));
```

## Третий вид чипа — по значению
### 1. Хелпер листьев (рядом с `ColumnDialogLeaves`) в `ClayGrid.Filtering.cs`
```csharp
/// <summary>Активные листья фильтра по значению — для отдельных чипов в панели.</summary>
private IEnumerable<ValueFilter> ValueFilterLeaves =>
    _filterRoot.Nodes.OfType<ValueFilter>().Where(vf => vf.HasValue);
```

### 2. Чипы значения в трее (`ClayGrid.razor`, блок `@if (_filterTrayExpanded)`)
Рендерить **всегда** (это отдельный вид, не зависит от `HasComposite`) — поставить
цикл сразу после кнопки «Настроить фильтр», до `@if (HasComposite)`:
```razor
@foreach (var vf in ValueFilterLeaves.ToList())
{
    var vfSql  = vf.Column;
    var vfDisp = _columnBySqlName.TryGetValue(vfSql, out var vm) ? vm.DisplayName : vfSql;
    <div class="filter-chip" style="cursor:pointer"
         @onclick="async () => await ((IClayGrid)this).OpenValueFilterDialog(vfSql)">
        <span class="chip-label">@vfDisp: выбраны значения</span>
        <span @onclick:stopPropagation="true">
            <MudIconButton Icon="@Icons.Material.Filled.Close"
                           Size="Size.Small"
                           OnClick="async () => await RemoveValueFilter(vfSql)"
                           Class="chip-remove-btn pa-0"
                           Style="width:18px;height:18px"
                           title="Снять фильтр по значению" />
        </span>
    </div>
}
```
Формат подписи чипа: **«Название колонки: выбраны значения»** (`@vfDisp: выбраны значения`).
Клик по чипу переоткрывает диалог значения; крестик — `RemoveValueFilter` (уже есть).

### 3. Учесть значение-фильтры в `_hasAnyFilter`
В том же блоке (строка ~232) значение-фильтры не дают сегментов, поэтому подсветка
трея и скрытие подсказки их не видят. Заменить:
```razor
var _hasAnyFilter = _filterSegments.Count > 0 || ValueFilterLeaves.Any();
```

## Взаимоисключение (а) чип-по-колонке ⟷ (в) по значению
Требование: если стоит фильтр по значению — фильтр по колонке через
чип/перетаскивание задать нельзя, и наоборот.

- **Значение → блок колонки-условия.** В начале `OpenFilterDialog(sqlName,
  displayName, initialOperator)` (это единая воронка для drag-drop, ⋮ «Фильтровать»
  и клика по чипу) добавить guard:
```csharp
if (_filterRoot.Nodes.OfType<ValueFilter>()
        .Any(vf => string.Equals(vf.Column, sqlName, StringComparison.OrdinalIgnoreCase) && vf.HasValue))
{
    Snackbar.Add($"На колонке «{displayName}» установлен фильтр по значению. " +
                 "Снимите его, чтобы задать фильтр по условию.", Severity.Info);
    return;
}
```
  Это перекрывает и перетаскивание в трей, и ⋮-меню, и редактирование чипа.

- **Колонка-условие → блок значений.** Уже реализовано в диалоге значения (V6,
  треб. 8–9): при наличии `ExistingConditionFilter` список значений заблокирован,
  а кнопка «Применить» скрыта. Дополнительно `ApplyValueFilter` удаляет условие
  колонки — оставить как есть (страховка), поведения не меняет.

> Правило взаимоисключения касается только пары «чип-по-колонке ⟷ по значению».
> Настраиваемый (составной) фильтр — отдельный вариант и под это правило не
> подпадает.

## Проверка
- [ ] При активном фильтре по значению в трее — непустой чип «Колонка: выбраны
      значения», трей подсвечен, подсказка не показывается.
- [ ] Клик по чипу открывает диалог значения; крестик снимает фильтр.
- [ ] `ValueFilter` больше не переводит трей в режим составного фильтра
      (`HasComposite` его игнорирует); пустой чип не появляется.
- [ ] Перетаскивание колонки / ⋮ «Фильтровать» по колонке с активным фильтром по
      значению — блокируется с уведомлением; и наоборот, значения недоступны при
      установленном условии.
- [ ] `dotnet build` без ошибок.
