using Clayzor.Lib.Entities.DynamicGrid;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Clayzor.Lib.Web.Controls.Components.Grid.Filter;
using Clayzor.Lib.Web.Controls.Services;
using Dapper;
using Microsoft.JSInterop;
using MudBlazor;

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

    /// <summary>SELECT всех строк источника без ROW_NUMBER() и пагинации.</summary>
    private string BuildDynamicSelectAllSql(string? where, string? orderBy)
    {
        var sql = $"SELECT * FROM ({SelectSql}) _src";
        if (!string.IsNullOrWhiteSpace(where))   sql += $" WHERE {where}";
        if (!string.IsNullOrWhiteSpace(orderBy)) sql += $" ORDER BY {orderBy}";
        return sql;
    }

    /// <summary>
    /// Строки текущей страницы. Без группировки — то, что уже в Items.
    /// С группировкой — для каждой группы на странице догружаются ВСЕ детальные строки,
    /// игнорируя пагинацию (как BuildExportRows в статике): печатать половину группы бессмысленно.
    /// </summary>
    public async Task<List<IClayGridRow>> BuildDynamicExportRowsForCurrentPage()
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

            // GroupKeys — строки; после GN2 "" означает NULL-ключ → null для IS NULL (GN3)
            var rawKeys = gh.GroupKeys
                .Select(k => k.Length == 0 ? null : (object?)k).ToList();
            var keyWhere = ClayGroupingEngine.BuildGroupKeyWhere(exprs, rawKeys, "dk", out var keyParams);
            foreach (var (name, value) in keyParams)
                detailParams.Add(name, value);

            var detailWhere = keyWhere.Length > 0
                ? ClayDataQuery.CombineWhere(where, keyWhere)
                : where;

            var sql  = BuildDynamicSelectAllSql(detailWhere, detailOrder);
            var rows = await DynamicSql.QueryRowsAsync(Db, sql, detailParams);
            result.AddRange(rows.Select(r => (IClayGridRow)new ClayDynamicRow(r)));
        }

        return result;
    }

    /// <summary>Все строки по текущему запросу, без пагинации.</summary>
    public async Task<List<IClayGridRow>> BuildDynamicExportRowsForAll()
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

    /// <summary>
    /// Строки выбранных записей. В группированном режиме групповые заголовки сохраняются
    /// для групп, где есть хотя бы одна выбранная запись (счётчик в заголовке — ПОЛНЫЙ
    /// размер группы, как в статике).
    /// </summary>
    public async Task<List<IClayGridRow>> BuildDynamicExportRowsForSelected(IReadOnlyCollection<int> selectedIds)
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
        var groupRows  = ClayGroupRowMapper.MapRows(rawGroups, exprs.Count);
        var aggregates = ClayGroupingEngine.BuildAggregates(groupRows);
        var roots      = ClayGroupingEngine.BuildTree(aggregates);
        ClayGroupingEngine.ComputeParentCounts(roots);

        var countLookup = new Dictionary<string, int>();
        CollectDynamicGroupCounts(roots, countLookup);

        // Запрос 2: строки — С idFilter, если он есть
        var rowsWhere = idFilter is null ? where : ClayDataQuery.CombineWhere(where, idFilter);
        var rawRows   = await DynamicSql.QueryRowsAsync(Db, BuildDynamicSelectAllSql(rowsWhere, orderBy), dp);

        // C# interleaving (до GN4 — как в BuildAllGroupedRowsForExcel)
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
                var fullKey = string.Join("", keys);

                result.Add(new GroupHeaderRow
                {
                    DisplayValue = ResolveGroupDisplayValue(exprs[depth], keys[depth] ?? ""),
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

    // ── Печать ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Читатель ячеек динамического грида. Создаётся на каждый экспорт: _clientOffset
    /// может измениться (GF11), справочники — нет, но они передаются по ссылке.
    /// </summary>
    private ClayDynamicCellReader CreateDynamicCellReader()
        => new(_dynamicCols, _dynamicLookups, _dynamicIconLookups, _clientOffset);

    /// <summary>Раскрытые группы для печати. Пусто, если группировки нет.</summary>
    private HashSet<string> DynamicExpandedGroups => _dynamicExpandedGroups;

    private async Task<string> BuildDynamicPrintHtmlForCurrentPage(
        IReadOnlyList<ClayColumnMeta> columns, string? filterDescription, string? groupDescription)
    {
        var rows = await BuildDynamicExportRowsForCurrentPage();
        return ClayGridPrintHtmlGenerator.Build(
            Title, columns, rows, CreateDynamicCellReader(), DynamicExpandedGroups,
            filterDescription, groupDescription);
    }

    private async Task<string> BuildDynamicPrintHtmlForAll(
        IReadOnlyList<ClayColumnMeta> columns, string? filterDescription, string? groupDescription)
    {
        var rows = await BuildDynamicExportRowsForAll();
        return ClayGridPrintHtmlGenerator.Build(
            Title, columns, rows, CreateDynamicCellReader(), DynamicExpandedGroups,
            filterDescription, groupDescription);
    }

    private async Task<string> BuildDynamicPrintHtmlForSelected(
        IReadOnlyList<ClayColumnMeta> columns, IReadOnlyCollection<int> selectedIds,
        string? filterDescription, string? groupDescription)
    {
        var rows = await BuildDynamicExportRowsForSelected(selectedIds);
        return ClayGridPrintHtmlGenerator.Build(
            Title, columns, rows, CreateDynamicCellReader(), DynamicExpandedGroups,
            filterDescription, groupDescription);
    }

    // ── Excel ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Выгрузка в Excel в динамическом режиме. Аналог ClayGridPageBase.ExcelExportAsync:
    /// тот же генератор, те же снекбары и имя файла, но строки грузятся через
    /// ClayGrid.Dynamic.Export, а ячейки читаются ClayDynamicCellReader.
    /// </summary>
    private async Task DynamicExcelExportAsync(ExcelExportRequest request)
    {
        try
        {
            var columns = request.VisibleColumns;
            if (columns.Count == 0) return;

            var rowsToExport = request.Mode switch
            {
                ExcelExportMode.CurrentPage => await BuildDynamicExportRowsForCurrentPage(),
                ExcelExportMode.Selected    => await BuildDynamicExportRowsForSelected(request.SelectedIds),
                ExcelExportMode.All         => await BuildDynamicExportRowsForAll(),
                _                           => await BuildDynamicExportRowsForCurrentPage(),
            };

            if (rowsToExport.Count == 0)
            {
                Snackbar.Add("Нет данных для выгрузки", Severity.Warning);
                return;
            }

            var bytes = ClayGridExcelGenerator.ExportToExcel(
                request.Title, columns, rowsToExport, CreateDynamicCellReader(),
                DynamicExpandedGroups, request.FilterDescription, request.GroupDescription);

            var base64   = Convert.ToBase64String(bytes);
            var fileName = $"{ClayGridExportFileName.Sanitize(request.Title)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            await JS.InvokeVoidAsync("clayGridExcel.downloadFile", fileName, base64);
            Snackbar.Add($"Файл «{fileName}» выгружен", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Ошибка выгрузки: {ex.Message}", Severity.Error);
        }
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
