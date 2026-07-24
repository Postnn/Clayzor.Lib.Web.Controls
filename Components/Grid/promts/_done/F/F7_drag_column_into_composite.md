# F7. Перетаскивание колонки при активном составном фильтре

Файлы: `Components/Grid/ClayGrid.Filtering.cs`,
`Components/Grid/Filter/ClayFilterExpression.razor(.cs)`,
`Components/Grid/ClayDataQuery.cs` (транзиентный флаг на `ColumnFilter`).
Реализует п.2.2. Опирается на F6 (текст вместо чипов, фокус на значение).

## Поведение
- **Нет составных условий** → как сейчас: `OpenFilterDialog(sqlName)` (диалог колонки),
  лист `Source=ColumnDialog`.
- **Есть составные условия** → открыть **диалог настраиваемого фильтра**, предварительно
  добавив условие по перетащенной колонке **на верхний уровень через И**, сужая весь
  фильтр. Если корень уже `И` (или пуст) — добавить лист в корень. Если корень `ИЛИ` —
  **обернуть**: новый корень `И(СтароеДерево, НовыйЛист)`.

Мутировать `_filterRoot` до подтверждения нельзя (отмена не должна менять фильтр) —
строим **кандидат-дерево** (копию) и открываем диалог на нём; на «Применить»
`_filterRoot` заменяется результатом, на «Отмена» — остаётся прежним.

## `ClayGrid.Filtering.cs`
```csharp
private bool HasComposite =>
    _filterRoot.Nodes.Any(n => n is not ColumnFilter cf || cf.Source != ClayFilterSource.ColumnDialog);

private async Task OnFilterTrayDrop(DragEventArgs e)
{
    var sql = ClayDragState.DraggedColumn;
    ClayDragState.DraggedColumn = null;
    if (string.IsNullOrEmpty(sql)) return;
    if (!_columnBySqlName.TryGetValue(sql, out var cm) || !cm.Filterable) return;

    if (HasComposite)
        await OpenCompositeFilterDialog(BuildTreeWithColumnAnded(sql));   // см. ниже
    else
        await OpenFilterDialog(sql, cm.DisplayName);                      // прежнее поведение
}

// Копия дерева с новым условием по колонке, приклеенным через И на верхнем уровне.
private ClayFilterGroupNode BuildTreeWithColumnAnded(string sql)
{
    var clone = (ClayFilterGroupNode)_filterRoot.Clone();
    var meta  = _columnBySqlName[sql];
    var leaf  = new ColumnFilter
    {
        Column   = sql,
        Operator = meta.Type.DefaultOperator,   // дескриптор из задачи 03
        Source   = ClayFilterSource.CompositeDialog,
        IsNew    = true,                         // транзиентный флаг фокуса (см. ниже)
    };

    if (clone.Nodes.Count == 0 || clone.Logic == LogicalOperator.And)
    {
        clone.Logic = LogicalOperator.And;       // на случай пустого корня
        clone.Nodes.Add(leaf);
        return clone;
    }
    // Корень = ИЛИ → обернуть, чтобы новое условие сужало весь фильтр
    return new ClayFilterGroupNode
    {
        Logic = LogicalOperator.And,
        Nodes = { clone, leaf },
    };
}
```
`OpenCompositeFilterDialog` доработать: принимать необязательный «seed»-корень;
если задан — открывать диалог на нём (передавать в параметр `Root`), иначе на `_filterRoot`.
На «Применить» — `_filterRoot = result` как сейчас.

## Транзиентный флаг фокуса на `ColumnFilter` (ClayDataQuery.cs)
```csharp
[System.Text.Json.Serialization.JsonIgnore]
public bool IsNew { get; set; }   // UI-подсказка: свежедобавленное условие → фокус на значение
```
`Clone()` этот флаг **не** копирует (по умолчанию false у копии). В сериализацию (задача 12)
не попадает благодаря `[JsonIgnore]`.

## Автофокус нового условия (ClayFilterExpression)
Для только что добавленного условия (поле уже задано перетаскиванием) фокус сразу в
«Значение»:
```csharp
protected override void OnAfterRender(bool firstRender)
{
    if (firstRender && Node.IsNew)
    {
        Node.IsNew = false;
        _valueKey++;          // из F6: ремоунт редактора значения
        _focusValue = true;   // из F6: автофокус
        StateHasChanged();
    }
    if (_focusValue) _focusValue = false;   // сброс из F6
}
```
(Механика `_valueKey`/`_focusValue`/`AutoFocus` — из F6, п.3.)

## Критерии
- [ ] Нет составных условий → перетаскивание работает как раньше (диалог колонки, чип).
- [ ] Есть составные условия → перетаскивание открывает диалог настраиваемого фильтра
      с новым условием по колонке, приклеенным через И на верхнем уровне.
- [ ] Корень `ИЛИ` → дерево обёрнуто `И(Старое, Новое)`; корень `И`/пуст → лист добавлен в корень.
- [ ] Отмена диалога не меняет действующий фильтр.
- [ ] У нового условия курсор сразу в «Значение».
- [ ] `dotnet build` без ошибок.
