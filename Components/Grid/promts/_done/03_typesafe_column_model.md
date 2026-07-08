# 03. Типобезопасная модель колонок (дескриптор типа)

## Проблема
Поведение, зависящее от типа колонки, размазано по четырём `switch`:
`MapClrTypeToColumnType` (CLR→enum), `ColumnFilterOperatorList` (enum→операторы),
редакторы в `ClayColumnFilterDialog` (enum→контрол), `ClayDataQuery.BuildSingleClause`
(оператор/значение→SQL). Date/Decimal (задача 01) пришлось править во всех четырёх.

## Решение (без переименования `ColumnType`)
`ColumnType` остаётся «видом». Рядом — **дескриптор**, единая точка типозависимого
поведения; метаданные колонки несут дескриптор (один источник истины вместо
параллельного `FilterColumnTypes`-словаря).

### Новые файлы `Components/Grid/ColumnTypes/`
`ColumnTypeDescriptor.cs`:
```csharp
public abstract class ColumnTypeDescriptor
{
    public abstract ColumnType Kind { get; }
    public abstract Type ClrType { get; }
    public abstract IReadOnlyList<ColumnFilterOperator> Operators { get; }
    public virtual ColumnFilterOperator DefaultOperator => Operators[0];
    public virtual bool OperatorTakesValue(ColumnFilterOperator op) => op is not (
        ColumnFilterOperator.IsEmpty or ColumnFilterOperator.IsNotEmpty
        or ColumnFilterOperator.IsNull or ColumnFilterOperator.IsNotNull);
    public abstract object? Parse(string? raw);     // инвариантная культура
    public abstract string  Format(object? value);
    public virtual  object? ToParameter(object? value) => value;
}
```
По классу на тип: `TextColumnType`, `NumberColumnType`(int), `DecimalColumnType`,
`BooleanColumnType`, `DateColumnType`. Каждый задаёт `Kind/ClrType/Operators`
(из `ColumnFilterOperatorList`) и `Parse/Format`.

`ColumnTypeRegistry.cs`:
- `FromClr(Type)` — замена `MapClrTypeToColumnType` (Nullable разворачивать;
  DateTime/DateOnly/DateTimeOffset→Date; decimal/double/float→Decimal;
  целочисленные→Number; bool→Boolean; иначе→Text).
- `FromKind(ColumnType)` — для десериализации фильтра (задача 12).
- Дескрипторы без состояния → синглтоны/кеш.

### Правки существующего кода
- `ClayColumnMeta` += `public ColumnTypeDescriptor Type { get; init; }`;
  грид резолвит дескриптор при регистрации колонки.
- `ClayGridPageBase.MapClrTypeToColumnType` → делегирует в `ColumnTypeRegistry.FromClr`.
  `FilterColumnTypes` оставить как **производное** (`SqlName→Kind`) для совместимости.
- `ClayDataQuery.BuildSingleClause` — оставить один `switch` по **оператору**
  (фрагмент SQL), значение прогонять через `descriptor.ToParameter(value)`.
- Редактор значения (задача 08) — один `switch` по `Kind`; операторы/парсинг — у дескриптора.

### Что это даёт
Новый тип колонки = один класс-дескриптор + строка в реестре. Три из четырёх
switch исчезают; четвёртый (рендер редактора) локализован в одном UI-компоненте.

### Необязательно (полная полиморфность)
Можно добавить `RenderFragment BuildEditor(...)` в дескриптор и убрать 4-й switch —
ценой протечки UI в класс-данные. Рекомендация: дескриптор = данные, рендер = один switch.

## Критерии
- [ ] Text/Number/Boolean/Date/Decimal фильтруются как прежде.
- [ ] Добавление типа не требует правки операторных/парсинг/SQL switch.
- [ ] `ClayColumnMeta.Type` — единственный источник; `FilterColumnTypes` производный.
- [ ] Парсинг/формат — инвариантная культура, централизованно.
- [ ] `dotnet build` без ошибок; существующие гриды работают.
