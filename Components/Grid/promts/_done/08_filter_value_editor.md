# 08. Общий редактор значения фильтра (ClayFilterValueEditor)

Единый редактор значения по типу колонки — чтобы не дублировать логику между
`ClayColumnFilterDialog` и новым диалогом настраиваемого фильтра (задача 09).
Сырой Mud допустим (как в существующих диалогах библиотеки).

## Новый файл `Components/Grid/Filter/ClayFilterValueEditor.razor` (+ `.razor.cs`)
- Параметры: `ColumnTypeDescriptor Type` (задача 03), `@bind-Value (object?)`,
  необязательный список вариантов `IReadOnlyList<ClayFilterOption>? Options`
  (для value-picker по Text/Number), текущий `ColumnFilterOperator Operator`.
- Рендер — **один** `switch` по `Type.Kind`:
  - Text → `MudTextField`;
  - Number → `MudNumericField<int?>`;
  - Decimal → `MudNumericField<decimal?>`;
  - Date → `MudDatePicker`;
  - Boolean → `MudSelect<bool?>`;
  - если задан `Options` → `MudSelect`/`ClayComboBox`-стиль (SQL/тип не меняются).
- Если `!Type.OperatorTakesValue(Operator)` (IsEmpty/IsNotEmpty/IsNull/IsNotNull) —
  редактор **скрывается**, `Value = null`.
- Биндинг `object? ↔ T` — адаптер на `Type.Parse`/`Type.Format` (инвариантная культура);
  на корневом контроле `@key` из `SqlName`+`Kind` — пересоздание при смене типа.

## Тип вариантов (рядом)
```csharp
public sealed class ClayFilterOption { public object? Value { get; init; } public string Label { get; init; } = ""; }
```

## (Желательно) refactor `ClayColumnFilterDialog`
Перевести его редакторы значения на `ClayFilterValueEditor` — единое поведение для
обоих диалогов. Не блокирующее требование; если рискованно — сделать в отдельный заход.

## Критерии
- [ ] Редактор корректен для Text/Number/Decimal/Date/Boolean и для value-picker.
- [ ] Операторы без значения скрывают редактор и обнуляют `Value`.
- [ ] `object? Value` биндится без ошибок (адаптер + `@key`, инвариантная культура).
- [ ] `dotnet build` без ошибок.
