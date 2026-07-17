> Часть плана «Печать и Excel динамического грида». Перед началом прочитай **GE0_README_dynamic_export.md** и **_readme_grid_dynamic.md**. Требует выполненных **GE2**, **GF16**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GE3 — `ClayGrid.Dynamic.Export.cs`: загрузка строк для экспорта

Прочитать перед началом (обязательно, до написания кода):

- **`Components/Grid/ClayGridPageBase.Export.Print.cs`** — `BuildAllRowsForPrint`,
  `BuildAllFlatRowsForPrint`, `BuildAllGroupedRowsForPrint`. Эталон для «все данные».
- **`Components/Grid/ClayGridPageBase.Export.Excel.cs`** — `BuildAllRowsForExcel`,
  `BuildAllGroupedRowsForExcel` (обрати внимание: там **C# interleaving** вместо N запросов на
  группу), `BuildExportRows`.
- **`Components/Grid/ClayGridPageBase.Export.Selected.cs`** — `BuildAllRowsForSelected`,
  `BuildAllFlatRowsForSelected`, `BuildAllGroupedRowsForSelected`, `CollectCounts`.
- `Components/Grid/ClayGrid.Dynamic.cs` — `LoadDynamicData`, `SelectSql`, `DefaultOrder`,
  `SearchColumns`, `_dynamicKnownColumns`, `_dynamicDef.IdColumn`, `_lastQuery`.
- `Components/Grid/Dynamic/ClayDynamicRow.cs`, `Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs`.
- `Components/Grid/ClayGroupingEngine.cs`.
- **`GE0_README_dynamic_export.md`, раздел «Модель доверия».**

## Задача

Дать динамическому режиму три набора строк, которые в статике готовит `ClayGridPageBase`:

| Что | Эталон | Кто вызовет |
|---|---|---|
| текущая страница | `BuildExportRows()` | GE4, GE5 |
| все данные по запросу | `BuildAllRowsForPrint()` / `BuildAllRowsForExcel()` | GE4, GE5 |
| выбранные записи | `BuildAllRowsForSelected(ids)` | GE4, GE5 |

Отличия динамики от эталона ровно два, как и в GG: строки берутся через `DynamicSql`, а не
`Db.QueryAsync<T>`, и заворачиваются в `ClayDynamicRow`, а не `DetailRow<T>`.

**Про группировку.** Если `GG1`–`GG7` не выполнены, `Groupable = false` (GF14), `_groupColumns`
всегда пуст, и группированные ветки этого файла никогда не вызовутся. Это НЕ повод их не писать:
они оживут вместе с GG7. Но и проверить их на этом шаге будет нечем — так и написано в блоке
«Проверка».

## Изменить/создать

Создать `Components/Grid/ClayGrid.Dynamic.Export.cs`:

```csharp
using Clayzor.Lib.Entities.DynamicGrid;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Dapper;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Загрузка строк для печати и выгрузки в Excel в динамическом режиме.
/// Аналог ClayGridPageBase.Export.* : та же логика, но источник строк — DynamicSql,
/// а обёртка — ClayDynamicRow вместо DetailRow&lt;T&gt;.
/// </summary>
public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>
    /// WHERE + параметры текущего запроса (поиск + фильтр). Собирается так же,
    /// как в LoadDynamicData — экспорт обязан выгружать ровно то, что видно в гриде.
    /// </summary>
    private (string? Where, DynamicParameters Params) BuildDynamicExportWhere()
    {
        var query = _lastQuery;
        var dp    = new DynamicParameters();
        dp.Add("search", $"%{query.SearchText}%");     // GF16

        var searchWhere = query.BuildWhereClause(SearchColumns);
        var filterWhere = ClayCompositeSqlBuilder.Build(query.CompositeFilter, dp, _dynamicKnownColumns);
        return (ClayDataQuery.CombineWhere(searchWhere, filterWhere), dp);
    }
```

Проверь имя поля с последним запросом (`_lastQuery`) по `ClayGrid.razor.cs`. **Если его нет —
ОСТАНОВИСЬ и скажи**, не изобретай своё: экспорт обязан использовать тот же запрос, что и грид.

**1. Текущая страница:**

```csharp
    /// <summary>
    /// Строки текущей страницы. Без группировки — то, что уже в Items.
    /// С группировкой — для каждой группы на странице догружаются ВСЕ детальные строки,
    /// игнорируя пагинацию (как BuildExportRows в статике): печатать половину группы бессмысленно.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildDynamicExportRowsForCurrentPage()
    {
        var query = _lastQuery;
        if (!query.GroupEnabled || query.GroupColumns.Count == 0)
            return (Items ?? []).OfType<IClayGridRow>().ToList();

        var (where, dp) = BuildDynamicExportWhere();
        var exprs       = query.GroupColumns.ToList();
        var orderBy     = query.BuildOrderBy(DefaultOrder);
        var detailOrder = ClayGroupingEngine.BuildDetailOrder(orderBy, exprs, DefaultOrder);

        var result = new List<IClayGridRow>();

        foreach (var row in Items ?? [])
        {
            if (row is not GroupHeaderRow gh)
                continue;                       // детали текущей страницы перезагрузим целиком

            result.Add(gh);

            if (!_dynamicExpandedGroups.Contains(gh.FullKey)) continue;
            if (gh.GroupKeys.Count != exprs.Count) continue;   // промежуточный уровень — детали ниже

            var detailParams = new DynamicParameters();
            detailParams.AddDynamicParams(dp);
            var keyParts = gh.GroupKeys
                .Select((k, i) => { detailParams.Add($"dk{i}", k); return $"{exprs[i]} = @dk{i}"; })
                .ToList();
            var detailWhere = ClayDataQuery.CombineWhere(where, string.Join(" AND ", keyParts));

            var sql  = BuildDynamicSelectAllSql(detailWhere, detailOrder);
            var rows = await DynamicSql.QueryRowsAsync(Db, sql, detailParams);
            result.AddRange(rows.Select(r => (IClayGridRow)new ClayDynamicRow(r)));
        }

        return result;
    }
```

**2. Общий построитель «SELECT всё без пагинации»** — им пользуются три метода ниже:

```csharp
    /// <summary>SELECT всех строк источника без ROW_NUMBER() и пагинации.</summary>
    private string BuildDynamicSelectAllSql(string? where, string? orderBy)
    {
        var sql = $"SELECT * FROM ({SelectSql}) _src";
        if (!string.IsNullOrWhiteSpace(where))   sql += $" WHERE {where}";
        if (!string.IsNullOrWhiteSpace(orderBy)) sql += $" ORDER BY {orderBy}";
        return sql;
    }
```

**3. Все данные** (плоско — один запрос; с группировкой — агрегат + один плоский запрос +
C# interleaving, как `BuildAllGroupedRowsForExcel`, а НЕ N запросов на группу):

```csharp
    /// <summary>Все строки по текущему запросу, без пагинации.</summary>
    private async Task<List<IClayGridRow>> BuildDynamicExportRowsForAll()
    {
        var query = _lastQuery;
        var (where, dp) = BuildDynamicExportWhere();
        var orderBy     = query.BuildOrderBy(DefaultOrder);

        if (!query.GroupEnabled || query.GroupColumns.Count == 0)
        {
            var rows = await DynamicSql.QueryRowsAsync(Db, BuildDynamicSelectAllSql(where, orderBy), dp);
            return rows.Select(r => (IClayGridRow)new ClayDynamicRow(r)).ToList();
        }

        return await BuildDynamicGroupedExportRows(where, dp, orderBy, idFilter: null);
    }
```

**4. Выбранные записи:**

```csharp
    /// <summary>
    /// Строки выбранных записей. В группированном режиме групповые заголовки сохраняются
    /// для групп, где есть хотя бы одна выбранная запись (счётчик в заголовке — ПОЛНЫЙ
    /// размер группы, как в статике).
    /// </summary>
    private async Task<List<IClayGridRow>> BuildDynamicExportRowsForSelected(IReadOnlyCollection<int> selectedIds)
    {
        if (selectedIds.Count == 0) return [];

        var idColumn = _dynamicDef?.IdColumn;
        if (string.IsNullOrWhiteSpace(idColumn) || !_dynamicKnownColumns.Contains(idColumn))
            return [];      // белый список: IdColumn идёт в текст SQL

        var query = _lastQuery;
        var (where, dp) = BuildDynamicExportWhere();
        var orderBy     = query.BuildOrderBy(DefaultOrder);

        var idParams = new List<string>();
        int idx = 0;
        foreach (var id in selectedIds)
        {
            var pName = $"sid{idx++}";
            dp.Add(pName, id);
            idParams.Add($"@{pName}");
        }
        var idFilter = $"{idColumn} IN ({string.Join(",", idParams)})";

        if (!query.GroupEnabled || query.GroupColumns.Count == 0)
        {
            var fullWhere = ClayDataQuery.CombineWhere(where, idFilter);
            var rows = await DynamicSql.QueryRowsAsync(Db, BuildDynamicSelectAllSql(fullWhere, orderBy), dp);
            return rows.Select(r => (IClayGridRow)new ClayDynamicRow(r)).ToList();
        }

        return await BuildDynamicGroupedExportRows(where, dp, orderBy, idFilter);
    }
```

**5. Группированный экспорт (общий для «все» и «выбранные»)** — два запроса + interleaving:

```csharp
    /// <summary>
    /// Группированные строки экспорта: агрегат (структура и счётчики групп) + один плоский
    /// запрос строк, отсортированных по группировочным колонкам. Заголовки групп строятся
    /// в C# однопроходным детектированием смены ключа — так же, как BuildAllGroupedRowsForExcel.
    /// Счётчики берутся из агрегата (ПОЛНЫЙ размер группы), даже когда выгружаются только
    /// выбранные записи.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildDynamicGroupedExportRows(
        string? where, DynamicParameters dp, string orderBy, string? idFilter)
    {
        var exprs = _lastQuery.GroupColumns.ToList();

        // Запрос 1: агрегат — БЕЗ idFilter, счётчики должны быть по всей группе
        var groupSql   = ClayGroupingEngine.BuildGroupAggregateSql(SelectSql, exprs, where, _lastQuery.SortColumns);
        var rawGroups  = await DynamicSql.QueryRowsAsync(Db, groupSql, dp);
        var groupRows  = ClayDynamicGroupMapper.MapRows(rawGroups);
        var aggregates = ClayGroupingEngine.BuildAggregates(groupRows);
        var roots      = ClayGroupingEngine.BuildTree(aggregates);
        ClayGroupingEngine.ComputeParentCounts(roots);

        var countLookup = new Dictionary<string, int>();
        CollectDynamicGroupCounts(roots, countLookup);

        // Запрос 2: строки — С idFilter, если он есть
        var rowsWhere = idFilter is null ? where : ClayDataQuery.CombineWhere(where, idFilter);
        var rawRows   = await DynamicSql.QueryRowsAsync(Db, BuildDynamicSelectAllSql(rowsWhere, orderBy), dp);

        // C# interleaving
        var result = new List<IClayGridRow>();
        string?[]? previousKeys = null;

        foreach (var raw in rawRows)
        {
            var currentKeys = exprs
                .Select(c => raw.TryGetValue(c, out var v) && v is not DBNull ? v?.ToString() : null)
                .ToArray();

            int firstDiff = 0;
            if (previousKeys is not null)
                while (firstDiff < previousKeys.Length
                       && firstDiff < currentKeys.Length
                       && string.Equals(previousKeys[firstDiff], currentKeys[firstDiff]))
                    firstDiff++;

            for (int depth = firstDiff; depth < exprs.Count; depth++)
            {
                var keys    = currentKeys.Take(depth + 1).ToList();
                var fullKey = string.Join("\u001F", keys);

                result.Add(new GroupHeaderRow
                {
                    DisplayValue = ResolveGroupDisplayValue(exprs[depth], keys[depth] ?? "") is { Length: > 0 } d
                                       ? d : "(пусто)",
                    FullKey      = fullKey,
                    ItemCount    = countLookup.GetValueOrDefault(fullKey),
                    Depth        = depth,
                    GroupKeys    = keys!,
                });
            }

            result.Add(new ClayDynamicRow(raw));
            previousKeys = currentKeys;
        }

        return result;
    }

    /// <summary>Рекурсивно собирает FullKey → ItemCount из дерева групп.</summary>
    private static void CollectDynamicGroupCounts(List<GridGroupNode> nodes, Dictionary<string, int> lookup)
    {
        foreach (var node in nodes)
        {
            lookup[node.Aggregate.FullKey] = node.Aggregate.ItemCount;
            CollectDynamicGroupCounts(node.Children, lookup);
        }
    }
}
```

Разбор мест, где легко ошибиться:

- **Агрегат считается БЕЗ `idFilter`, строки — С ним.** Заголовок группы обязан показывать
  «(12 шт.)» — реальный размер группы, даже если выгружаются 3 выбранные записи из неё. Так же
  делает `BuildAllGroupedRowsForSelected`.
- **Interleaving вместо N запросов.** Для «всех данных» N запросов на группу (как
  `BuildAllGroupedRowsForPrint`) — это N обращений к БД. Эталон для Excel уже решил это двумя
  запросами; повторяем его, а не печатный вариант.
- **`ResolveGroupDisplayValue`** — из GG6. Если GG6 не выполнен, метода нет: тогда подставь
  `keys[depth] ?? "(пусто)"` и **напиши в отчёте, что заголовки групп в экспорте покажут коды,
  пока не сделан GG6**. Не пиши второй справочный резолвер.
- **`ClayDynamicGroupMapper`** — из GG1. Если GG1 не выполнен, метода нет: ОСТАНОВИСЬ и скажи,
  что для группированного экспорта нужен GG1. Плоский экспорт от него не зависит — GE4/GE5
  можно делать и без него.
- **`(пусто)`** для NULL-ключа — как в эталоне. Не «улучшай» на пустую строку.
- **Строки страницы (`Items`) уже загружены** — в `BuildDynamicExportRowsForCurrentPage`
  плоская ветка переиспользует их без запроса к БД. Так же делает `BuildExportRows`.

## Не делай

Не вызывай `Db.QueryAsync` напрямую — только `DynamicSql` (`DynamicParameters` использовать
можно, это модель параметров). Не меняй `ClayGroupingEngine`, `ClayGridPageBase.Export.*`,
генераторы. Не трогай `ClayGrid.ExportMenu.cs` — это GE4/GE5. Не подставляй `IdColumn` в SQL
без проверки по `_dynamicKnownColumns`. Не собирай `IN (...)` конкатенацией значений — только
параметры `@sid{n}`. Не включай меню экспорта — это GE6.

## Проверка

**Юнит:** методы ходят в БД, юнитами не проверяются. Проверь то, что чисто:
`CollectDynamicGroupCounts` на дереве из GG1-теста → словарь со всеми `FullKey`, у корня —
сумма потомков.

**Ручная — только косвенная, меню экспорта ещё скрыто (GF15).** Временно добавь в
`InitDynamicMode`, в самый конец, отладочный вызов:

```csharp
// ВРЕМЕННО — убрать после проверки GE3
var _dbg = await BuildDynamicExportRowsForAll();
Console.WriteLine($"[GE3] rows={_dbg.Count}, headers={_dbg.OfType<GroupHeaderRow>().Count()}");
```

`?id=140&CLID=9`:

- в консоли `rows` = полному числу записей в источнике (не размеру страницы!), `headers=0`;
- в профайлере — ОДИН `SELECT * FROM (...) _src ORDER BY ...` без `ROW_NUMBER()`;
- навесить фильтр → `rows` уменьшилось, запрос ушёл с `WHERE`;
- ввести текст в поиск → `rows` уменьшилось, ошибки `@search` нет (GF16);
- **убрать временный код**, пересобрать.

**Группированные ветки на этом шаге не проверяются**, если не сделан GG7: `_groupColumns` пуст,
ветки недостижимы. Проверишь их в GE4/GE5 после GG7 — сценарии там расписаны. Так и напиши
в отчёте, не выдумывай проверку.

Статический режим (`MedicalTests.razor`): не затронут, новый файл — partial динамического грида.
