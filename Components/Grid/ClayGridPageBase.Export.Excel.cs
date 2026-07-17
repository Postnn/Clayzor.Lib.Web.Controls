using Dapper;
using Clayzor.Lib.Entities;
using Clayzor.Lib.Web.Controls.Services;
using Microsoft.JSInterop;
using MudBlazor;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public abstract partial class ClayGridPageBase<T> where T : Entity
{
    async Task IClayGridDataLoader.ExcelExportAsync(ExcelExportRequest request)
    {
        try
        {
            var columns = request.VisibleColumns;
            if (columns.Count == 0) return;

            List<IClayGridRow> rowsToExport;
            switch (request.Mode)
            {
                case ExcelExportMode.CurrentPage:
                    rowsToExport = await BuildExportRows();
                    break;
                case ExcelExportMode.Selected:
                    rowsToExport = await BuildAllRowsForSelected(new HashSet<int>(request.SelectedIds));
                    break;
                case ExcelExportMode.All:
                    rowsToExport = await BuildAllRowsForExport();
                    break;
                default:
                    rowsToExport = _rows;
                    break;
            }

            if (rowsToExport.Count == 0)
            {
                Snackbar.Add("Нет данных для выгрузки", Severity.Warning);
                return;
            }

            var bytes = ClayGridExcelGenerator.ExportToExcel(
                request.Title, columns, rowsToExport, typeof(T), _query.ExpandedGroups,
                request.FilterDescription, request.GroupDescription);

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

    /// <summary>
    /// Загружает ВСЕ строки для экспорта в Excel.
    /// Плоский режим — один запрос (без пагинации).
    /// Группировка — запрос агрегатов (GROUP BY) + один запрос всех строк,
    /// групповая структура строится в C# по отсортированным данным.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildAllRowsForExport()
    {
        if (_query.GroupEnabled && _query.GroupColumns.Count > 0)
            return await BuildAllGroupedRowsForExport();
        return await BuildAllFlatRowsForExport();
    }

    /// <summary>
    /// Режим группировки для Excel-экспорта всех данных.
    /// Делает ровно 2 SQL-запроса:
    /// 1. GROUP BY для агрегатов (количество записей в каждой группе)
    /// 2. SELECT * без пагинации, отсортированный по групповым колонкам + детальный порядок.
    /// Групповые заголовки (<see cref="GroupHeaderRow"/>) строятся в C# путём
    /// однопроходного детектирования смены группового ключа.
    /// В отличие от печатной версии (удалена в GB10) не делает N запросов
    /// на каждую листовую группу — два запроса + C#-interleaving.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildAllGroupedRowsForExport()
    {
        var selectSql     = Grid?.SelectSql     ?? string.Empty;
        var searchColumns = Grid?.SearchColumns ?? [];
        var defaultOrder  = Grid?.DefaultOrder  ?? string.Empty;

        var searchWhere    = _query.BuildWhereClause(searchColumns);
        var dp             = new DynamicParameters();
        dp.Add("search", $"%{_query.SearchText}%");
        var compositeWhere = BuildCompositeFilterClause(_query.CompositeFilter, dp);
        var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);

        var groupCols = _query.GroupColumns.ToList();

        // ── Запрос 1: GROUP BY агрегаты ─────────────────────────────
        var groupSql  = ClayGroupingEngine.BuildGroupAggregateSql(
            selectSql, groupCols, where, _query.SortColumns);

        // Dapper не мапит переменный набор K{i} на GridGroupRow — читаем словарями (GN1).
        var rawGroups = await Db.QueryAsync<dynamic>(groupSql, dp);
        var groupRows = ClayGroupRowMapper.MapRows(
            rawGroups.Cast<IDictionary<string, object?>>()
                     .Select(d => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(d)),
            groupCols.Count);

        var aggregates = ClayGroupingEngine.BuildAggregates(groupRows);
        var roots      = ClayGroupingEngine.BuildTree(aggregates);
        ClayGroupingEngine.ComputeParentCounts(roots);

        // FullKey → ItemCount (DFS по дереву — листовые и родительские узлы)
        var countLookup = new Dictionary<string, int>();
        CollectCounts(roots, countLookup);

        // ── Запрос 2: все строки одним запросом ─────────────────────
        var orderBy = _query.BuildOrderBy(defaultOrder);
        var flatSql = $"SELECT * FROM ({selectSql}) _src";
        if (!string.IsNullOrWhiteSpace(where))
            flatSql += $" WHERE {where}";
        if (!string.IsNullOrWhiteSpace(orderBy))
            flatSql += $" ORDER BY {orderBy}";

        var items = await Db.QueryAsync<T>(flatSql, dp);

        // ── C# interleaving: групповые заголовки + строки ───────────
        var result     = new List<IClayGridRow>();
        string?[]? previousKeys = null;

        foreach (var item in items)
        {
            var currentKeys = groupCols
                .Select(c => _propertyMap.TryGetValue(c, out var p)
                    ? p.GetValue(item)?.ToString()
                    : null)
                .ToArray();

            result.AddRange(ClayGroupingEngine.BuildInterleavedHeaders(currentKeys, previousKeys, countLookup));
            result.Add(new DetailRow<T> { Item = item });
            previousKeys = currentKeys;
        }

        return result;
    }

    /// <summary>
    /// Рекурсивно собирает <c>FullKey → ItemCount</c> из дерева групп
    /// (<see cref="GridGroupNode"/>), включая родительские и листовые узлы.
    /// </summary>
    private static void CollectCounts(List<GridGroupNode> nodes, Dictionary<string, int> lookup)
    {
        foreach (var node in nodes)
        {
            lookup[node.Aggregate.FullKey] = node.Aggregate.ItemCount;
            CollectCounts(node.Children, lookup);
        }
    }

    /// <summary>Печатная форма текущей страницы: строки — ровно экран (_rows), без догрузки.</summary>
    Task<string> IClayGridDataLoader.BuildPrintHtmlForCurrentPageAsync(
        IReadOnlyList<ClayColumnMeta> columns, string title,
        string? filterDescription, string? groupDescription)
    {
        return Task.FromResult(ClayGridPrintHtmlGenerator.Build(
            title, columns, _rows, typeof(T),
            filterDescription, groupDescription));
    }

    /// <summary>
    /// Строит список строк для экспорта текущей страницы. Если активна группировка —
    /// для каждой группы (листовой или промежуточной) загружает ВСЕ детальные строки
    /// (игнорируя пагинацию). На грид это не влияет — данные загружаются отдельным запросом.
    /// </summary>
    /// <summary>
    /// Строки для Excel «Текущая страница». Если активна группировка —
    /// для каждого заголовка группы на странице загружает ВСЕ строки её поддерева
    /// (игнорируя пагинацию и раскрытость). Печать текущей страницы берёт _rows напрямую. Заголовок, поддерево которого уже выгружено
    /// вместе с его предком, пропускается — иначе строки задваиваются.
    /// На грид не влияет: данные грузятся отдельным запросом.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildExportRows()
    {
        if (!_query.GroupEnabled || _query.GroupColumns.Count == 0)
            return _rows;

        var selectSql     = Grid?.SelectSql     ?? "";
        var defaultOrder  = Grid?.DefaultOrder  ?? "";
        var searchColumns = Grid?.SearchColumns ?? [];

        var searchWhere    = _query.BuildWhereClause(searchColumns);
        var dp             = new DynamicParameters();
        dp.Add("search", $"%{_query.SearchText}%");
        var compositeWhere = BuildCompositeFilterClause(_query.CompositeFilter, dp);
        var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);

        var groupCols = _query.GroupColumns.ToList();
        // BuildOrderBy при группировке начинается с группировочных колонок — интерливинг
        // вложенных заголовков требует именно такого порядка (НЕ BuildDetailOrder).
        var orderBy = _query.BuildOrderBy(defaultOrder);

        // Счётчики — из дерева последней загрузки. Агрегатные запросы на каждый заголовок не нужны.
        var countLookup = new Dictionary<string, int>();
        if (_groupTreeRoots is not null)
            CollectCounts(_groupTreeRoots, countLookup);

        var result  = new List<IClayGridRow>();
        var covered = new List<string>();

        foreach (var row in _rows)
        {
            if (row is not GroupHeaderRow header) continue;
            if (covered.Any(k => header.FullKey == k || header.FullKey.StartsWith(k + ''))) continue;

            result.Add(header);
            covered.Add(header.FullKey);

            var detailParams = new DynamicParameters();
            detailParams.AddDynamicParams(dp);

            // GroupKeys — строки; после GN2 "" означает NULL-ключ → null для IS NULL (GN3).
            // Ключей может быть меньше числа уровней: тогда это WHERE по поддереву.
            var rawKeys  = header.GroupKeys.Select(k => k.Length == 0 ? null : (object?)k).ToList();
            var keyWhere = ClayGroupingEngine.BuildGroupKeyWhere(groupCols, rawKeys, "dk", out var keyParams);
            foreach (var (name, value) in keyParams)
                detailParams.Add(name, value);

            var detailWhere = keyWhere.Length > 0
                ? ClayDataQuery.CombineWhere(where, keyWhere)
                : where;

            var sql = $"SELECT * FROM ({selectSql}) _src";
            if (!string.IsNullOrWhiteSpace(detailWhere)) sql += $" WHERE {detailWhere}";
            if (!string.IsNullOrWhiteSpace(orderBy))     sql += $" ORDER BY {orderBy}";

            var items = await Db.QueryAsync<T>(sql, detailParams);

            // previousKeys стартует с ключей заголовка — сам заголовок не продублируется,
            // вставятся только уровни ниже.
            IReadOnlyList<string?>? previousKeys = header.GroupKeys;

            foreach (var item in items)
            {
                var currentKeys = groupCols
                    .Select(c => _propertyMap.TryGetValue(c, out var p) ? p.GetValue(item)?.ToString() : null)
                    .ToArray();

                result.AddRange(ClayGroupingEngine.BuildInterleavedHeaders(currentKeys, previousKeys, countLookup));
                result.Add(new DetailRow<T> { Item = item });
                previousKeys = currentKeys;
            }
        }

        return result;
    }
}
