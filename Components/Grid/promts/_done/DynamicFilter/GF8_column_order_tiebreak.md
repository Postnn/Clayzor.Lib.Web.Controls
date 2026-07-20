> Часть серии «Багфиксы динамического режима ClayGrid». Перед началом прочитай **GF0_README_dynamic_fixes.md** и **_readme_grid_dynamic.md**. Требует выполненного **GF4**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GF8 — детерминированный порядок колонок при одинаковом `Порядок`

Прочитать перед началом: `Clayzor.Lib.Entities/DynamicGrid/ClayGridDefinitionData.cs` —
`BuildColumnsSql`; `Components/Grid/ClayGrid.Dynamic.cs` — сборка `gridCols` в `InitDynamicMode`
(после GF4); `Clayzor.Lib.Entities/DynamicGrid/ClayGridSchemaMap.cs` — `ColumnCols.ColumnId`,
`ColumnCols.Order`; `scripts/dynamic-grid/schema.sql` — `ЗапросыКолонки`.

## Дефект

`Порядок` в `ЗапросыКолонки` ничем не ограничен: ни `UNIQUE`, ни `NOT NULL`. Если у двух колонок
одного грида одинаковый `Порядок` (типовая ситуация после ручной правки справочника), их
взаимный порядок не определён:

- `BuildColumnsSql` строит `ORDER BY [Порядок]` — при равных ключах SQL Server не гарантирует
  стабильности, порядок может меняться от плана к плану;
- `InitDynamicMode` делает `.OrderBy(c => c.Order ...)` — `OrderBy` в LINQ to Objects стабилен,
  но стабилизирует он порядок, пришедший из SQL, то есть недетерминированный.

Наружу это лезет как «колонки иногда меняются местами сами по себе», а после GF5 ещё и
записывается в `cols+gridId`, то есть случайный порядок закрепляется у пользователя.

## Изменить/создать

**1.** `ClayGridDefinitionData.BuildColumnsSql` — добавить tie-breaker по коду колонки:

```csharp
public static string BuildColumnsSql(string columnsTable, ClayGridSchemaMap s)
{
    var c = s.Columns;
    return $"SELECT [{c.ColumnId}],[{c.GridId}],[{c.Column}],[{c.Header}],[{c.UrlKey}],[{c.Order}],[{c.Format}],[{c.Type}]"
         + $" FROM [{columnsTable}] WHERE [{c.GridId}] = @gridId"
         + $" ORDER BY [{c.Order}], [{c.ColumnId}]";
}
```

**2.** `ClayGrid.Dynamic.cs`, сборка `gridCols` — тот же tie-breaker в LINQ:

```csharp
var gridCols = _dynamicCols
    .Where(c => c.Type != (int)ClayColumnKind.ConditionBool
             && c.Type != (int)ClayColumnKind.ConditionList)
    .OrderBy(c => c.Order is > 0 ? 0 : 1)
    .ThenBy(c => c.Order ?? int.MaxValue)
    .ThenBy(c => c.ColumnId)
    .ToList();
```

Оба места нужны: SQL-сортировка — чтобы `_dynamicCols` был стабилен сам по себе, LINQ — потому
что там дополнительная группировка «видимые/скрытые» и полагаться на порядок источника нельзя.

## Не делай

Не добавляй `UNIQUE`-ограничение на `Порядок` в `schema.sql` — это справочник заказчика,
дубли там легальны. Не меняй семантику `Порядок` 0/NULL. Не трогай `ClayGridUserParamsData`.

## Проверка

- Юнит-тест (TG2, `BuildColumnsSql`): результат заканчивается на
  `ORDER BY [Порядок], [КодКолонки]`; при переопределённом `ClayGridSchemaMap` имена
  подставляются из карты, а не хардкодятся.
- Ручная: в `ЗапросыКолонки` для грида 140 выставить `Порядок = 2` двум колонкам, открыть
  `?id=140&CLID=9`, обновить страницу 5 раз → порядок колонок каждый раз один и тот же,
  колонка с меньшим `КодКолонки` идёт первой.
- Порядок остальных колонок (с уникальным `Порядок`) не изменился.
