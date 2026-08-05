# GA5 — Пагинация вне диапазона, поиск по decimal/дате-времени, N лишних INSERT на загрузку

## Контекст

Три независимых дефекта в динамическом режиме.

1. **Страница вне диапазона (`ClayGrid.Dynamic.cs`, `LoadDynamicFlatData`).** Считаются двумя
   запросами `QueryPagedRowsAsync` (по `PageNumber`) и `QueryCountAsync`. После смены фильтра,
   уменьшившего число записей, `_pageNumber` может указывать за пределы (`страница 5 из 2`) —
   тогда `QueryPagedRowsAsync` вернёт пусто, а пользователь увидит «нет данных», хотя они есть на
   странице 1. Нужно клампить `PageNumber` к `[1, totalPages]` и при коррекции перечитывать.
2. **Поиск по типам (`BuildSearchLikeExpr`).** Нет ветки для `Decimal` — дробные ищутся
   выражением по умолчанию (`col LIKE @q`), что на `decimal` даёт неявное приведение и часто не
   матчит; `CONVERT(nvarchar(30), col, 104)` для дат жёстко форматирует `dd.mm.yyyy`, теряя
   время — поиск по `DateTimeLocal`/`TimeLocal` со временем не находит.
3. **N последовательных INSERT (`SaveDynamicState`/`SaveParamIfChanged`).** `SaveDynamicState`
   вызывается в конце **каждой** `LoadDynamicData` и пишет до 6 параметров по одному
   `ExecuteAsync` на каждый изменившийся. При активной работе (сортировка → фильтр → страница)
   это десятки round-trip к БД. Нужен батч: один вызов на все изменившиеся параметры.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.cs`

**1.1. Кламп страницы.** В `LoadDynamicFlatData` заменить:

```csharp
var rows = await DynamicSql.QueryPagedRowsAsync(
    Db, _opt.SelectSql, where, orderBy, dp, query.PageNumber, query.PageSize);

TotalCount = await DynamicSql.QueryCountAsync(Db, _opt.SelectSql, where, dp);
Items      = rows.Select(r => (TEntity)(object)new ClayDynamicRow(r)).ToList();
```

на:

```csharp
TotalCount = await DynamicSql.QueryCountAsync(Db, _opt.SelectSql, where, dp);

// Кламп страницы: после сужающего фильтра PageNumber мог уйти за диапазон
var totalPages = query.PageSize > 0 && TotalCount > 0
    ? (int)Math.Ceiling((double)TotalCount / query.PageSize)
    : 1;
if (query.PageNumber > totalPages)
{
    query.PageNumber = totalPages;
    _pageNumber      = totalPages;
}
if (query.PageNumber < 1)
{
    query.PageNumber = 1;
    _pageNumber      = 1;
}

var rows = await DynamicSql.QueryPagedRowsAsync(
    Db, _opt.SelectSql, where, orderBy, dp, query.PageNumber, query.PageSize);
Items = rows.Select(r => (TEntity)(object)new ClayDynamicRow(r)).ToList();
```

(Count теперь считается до страницы — параметры `dp` те же, дополнительных изменений не нужно.
Транзакционную согласованность Count/Page не вводим — это отдельная задача; кламп решает
пользовательскую проблему «пустая страница».)

**1.2. Поиск по типам.** В `BuildSearchLikeExpr` добавить ветки. Заменить тело на:

```csharp
public static string BuildSearchLikeExpr(string column, int type, string? format)
{
    // Дата без времени
    if (type == (int)ClayColumnKind.Date)
        return $"CONVERT(nvarchar(30), {column}, 104) LIKE @q ESCAPE '\\'";

    // Дата+время / время: форматируем с временем (121 = yyyy-mm-dd hh:mi:ss)
    if (type == (int)ClayColumnKind.DateTimeLocal || type == (int)ClayColumnKind.TimeLocal)
        return $"CONVERT(nvarchar(30), {column}, 121) LIKE @q ESCAPE '\\'";

    // Число / дробное: приводим к строке
    if (type == (int)ClayColumnKind.Number || type == (int)ClayColumnKind.Decimal)
        return $"CAST({column} AS nvarchar(50)) LIKE @q ESCAPE '\\'";

    return $"{column} LIKE @q ESCAPE '\\'";
}
```

(Если поиск по дате в формате пользователя `dd.mm.yyyy` важнее ISO — оставить `104`, но тогда
для DateTime добавить второе выражение через OR с временем. Минимально: 121 находит и дату, и
время; для чисто-датного поля 104 остаётся.)

### Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.cs` — батч сохранения

**2.1.** Собрать изменившиеся параметры в один список и записать за один вызов. Переписать
`SaveDynamicState`:

```csharp
private async Task SaveDynamicState()
{
    if (_isSharedMode) return;   // choke point (был в SaveParamIfChanged)

    var opt = DynamicOpts.Value;
    var p   = (string prefix) => ClayGridUserParamsData.BuildParamName(prefix, _dynamicGridId);

    var candidates = new List<(string Name, string Value)>
    {
        (p(opt.ColumnsParamPrefix),  GridStateSerializer.SerializeColumns(_columnOrder, _columnById, _hiddenSqlNames)),
        (p(opt.SortingParamPrefix),  GridStateSerializer.SerializeSort(_sortState)),
        (p(opt.GroupingParamPrefix), GridStateSerializer.SerializeGroups(_groupColumns)),
        (p(opt.PageSizeParamPrefix), GridStateSerializer.SerializePageSize(_pageSize)),
        (p(opt.FilterParamPrefix),   GridStateSerializer.SerializeFilter(_filterRoot) ?? string.Empty),
    };
    var qksValue = SerializeQuickSearchColumns();
    if (qksValue is not null)
        candidates.Add((p(opt.QuickSearchParamPrefix), qksValue));

    var toSave = candidates
        .Where(c => !_dynamicForcedParamNames.Contains(c.Name))
        .Where(c => !(_dynamicSavedParams.TryGetValue(c.Name, out var cur) && cur == c.Value))
        .ToList();

    if (toSave.Count == 0) return;

    await ClayGridUserParamsData.SaveManyAsync(
        Db, _dynamicClid, toSave, opt.UserParamsTable, opt.Schema, sharedId: 0);

    foreach (var (name, value) in toSave)
        _dynamicSavedParams[name] = value;
}
```

**2.2.** Реализовать `ClayGridUserParamsData.SaveManyAsync` (файл
`Clayzor.Lib.Entities/DynamicGrid/ClayGridUserParamsData.cs`) как обёртку, выполняющую все
INSERT/UPSERT одним `ExecuteAsync` с массивом параметров (Dapper принимает `IEnumerable` в
`ExecuteAsync` и делает батч):

```csharp
public static async Task SaveManyAsync(
    DbManager db, int clid, IReadOnlyList<(string Name, string Value)> items,
    string table, ClayGridSchema schema, int sharedId)
{
    if (items.Count == 0) return;
    var sql = BuildInsertSql(table, schema); // тот же UPSERT, что в SaveAsync
    var rows = items.Select(it => new
    {
        clid,
        name  = it.Name,
        value = (object?)it.Value ?? DBNull.Value,
        shid  = sharedId,
    }).ToArray();
    await db.RunAsync(c => c.ExecuteAsync(sql, rows));
}
```

(Свериться с реальной сигнатурой существующего `SaveAsync`/`BuildInsertSql` и именами параметров
`clid/name/value/shid` — привести в точное соответствие. Если UPSERT-SQL использует другие имена
плейсхолдеров, подогнать анонимный объект под них.)

**2.3.** Метод `SaveParamIfChanged` больше не нужен для `SaveDynamicState`, но может
использоваться в других местах — проверить поиском. Если единственный вызывающий был
`SaveDynamicState`, пометить `SaveParamIfChanged` `[Obsolete]` или удалить.

## Критерии приёмки

- Сценарий: применить фильтр, сокращающий выборку до 3 записей, находясь на странице 5 →
  грид показывает данные (страница склампилась к 1), не «нет данных».
- Тест `BuildSearchLikeExpr`: `Decimal` → выражение содержит `CAST(... AS nvarchar`;
  `DateTimeLocal` → `CONVERT(..., 121)`.
- Профилирование/лог БД: одна операция сохранения на загрузку вместо до 6 отдельных.
- Существующие тесты параметров (`TG5_user_params_tests`) проходят; добавить тест на
  `SaveManyAsync` (батч из 2 параметров пишет обе строки).
