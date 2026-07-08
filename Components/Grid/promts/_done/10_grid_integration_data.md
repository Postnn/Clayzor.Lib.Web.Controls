# 10. Интеграция в ClayGrid: данные и загрузка

Единый источник истины — дерево `ClayFilterGroupNode`. Прежний словарь
`_activeFilters`, неявно объединяемый через AND, упраздняется (становится деревом).
Эта задача — только **данные/загрузка**; панель и маршрутизация — задача 11.

## Состояние грида (`ClayGrid.Filtering.cs`, см. задачу 05)
- `private ClayFilterGroupNode _filterRoot = new();` (корень `Logic=And`).
- Синхронизировать `_filterRoot` → `query.CompositeFilter`.

## `ClayDataQuery.cs`
- `public ClayFilterGroupNode? CompositeFilter { get; set; }`.

## `IClayGrid.cs`
```csharp
ClayFilterGroupNode? ActiveCompositeFilter { get; }
Task OpenCompositeFilterDialog();   // реализация UI — задача 11
```
(`AddFilterAsync(sqlName)` остаётся — открывает колоночный диалог и вставляет лист.)

## Пути загрузки (`ClayGridPageBase`, см. задачу 06)
- Заменить вызовы `_query.BuildColumnFilterClause(dp)` (≈7 мест: страница,
  группировка, экспорт, печать, выбранные, детали) на
  `ClayCompositeSqlBuilder.Build(_query.CompositeFilter, dp, knownColumns, columnNameMap)`
  (задача 07).
- `knownColumns` — множество `SqlName` зарегистрированных колонок (через `IClayGrid`/метаданные).
- Объединение с поиском — существующим `CombineWhere`.

## Критерии
- [ ] Параллельного словаря-фильтра (AND по умолчанию) больше нет; истина — дерево.
- [ ] Все пути загрузки учитывают дерево; пустой корень = без фильтрации.
- [ ] `dotnet build` без ошибок; данные грузятся корректно.
