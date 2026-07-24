using Dapper;
using Clayzor.Lib.Entities;
using Clayzor.Lib.Web.Controls.Components.Grid.Filter;
using Clayzor.Lib.Web.Controls.Services;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public abstract partial class ClayGridPageBase<T> where T : Entity
{
    async Task<string> IClayGridDataLoader.BuildPrintHtmlForSelectedAsync(
        IReadOnlyList<ClayColumnMeta> columns, string title,
        IReadOnlyList<int> selectedIds,
        string? filterDescription, string? groupDescription)
    {
        var rows = await BuildAllRowsForSelected(new HashSet<int>(selectedIds));
        return ClayGridPrintHtmlGenerator.Build(
            title, columns, rows, typeof(T),
            filterDescription, groupDescription);
    }

    /// <summary>
    /// Строит список строк, содержащий только выбранные сущности (по ID).
    /// В grouped-режиме сохраняет групповые заголовки для групп, в которых есть
    /// хотя бы одна выбранная запись.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildAllRowsForSelected(HashSet<int> selectedIds)
    {
        if (selectedIds.Count == 0) return [];

        if (_query.GroupEnabled && _query.GroupColumns.Count > 0)
            return await BuildAllGroupedRowsForSelected(selectedIds);
        return await BuildAllFlatRowsForSelected(selectedIds);
    }

    /// <summary>
    /// Плоский режим: загружает выбранные сущности по ID с учётом текущих фильтров.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildAllFlatRowsForSelected(HashSet<int> selectedIds)
    {
        var selectSql     = Grid?.Options.SelectSql     ?? string.Empty;
        var searchColumns = Grid?.Options.SearchColumns ?? [];

        var searchWhere    = _query.BuildWhereClause(searchColumns);
        var dp             = new DynamicParameters();
        dp.Add("search", $"%{_query.SearchText}%");
        var compositeWhere = BuildCompositeFilterClause(_query.CompositeFilter, dp);
        var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);

        var idParams = new List<string>();
        int idx = 0;
        foreach (var id in selectedIds)
        {
            var pName = $"sid{idx}";
            dp.Add(pName, id);
            idParams.Add($"@{pName}");
            idx++;
        }
        var idFilter  = $"{_idColumnName} IN ({string.Join(",", idParams)})";
        var fullWhere = ClayDataQuery.CombineWhere(where, idFilter);

        var sql = $"SELECT * FROM ({selectSql}) _src";
        if (!string.IsNullOrWhiteSpace(fullWhere))
            sql += $" WHERE {fullWhere}";

        var items = await Db.QueryAsync<T>(sql, dp);
        return items.Select(i => (IClayGridRow)new DetailRow<T> { Item = i }).ToList();
    }

    /// <summary>
    /// Группированный режим: загружает выбранные сущности, строит групповые заголовки
    /// через C# interleaving (как в BuildAllGroupedRowsForExcel).
    /// </summary>
    private async Task<List<IClayGridRow>> BuildAllGroupedRowsForSelected(HashSet<int> selectedIds)
    {
        var selectSql     = Grid?.Options.SelectSql     ?? string.Empty;
        var searchColumns = Grid?.Options.SearchColumns ?? [];
        var defaultOrder  = Grid?.Options.DefaultOrder  ?? string.Empty;

        var searchWhere    = _query.BuildWhereClause(searchColumns);
        var dp             = new DynamicParameters();
        dp.Add("search", $"%{_query.SearchText}%");
        var compositeWhere = BuildCompositeFilterClause(_query.CompositeFilter, dp);
        var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);
        var groupCols      = _query.GroupColumns.ToList();

        // Шаг 1: GROUP BY агрегаты (полная структура групп)
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

        var countLookup = new Dictionary<string, int>();
        CollectCounts(roots, countLookup);

        // Шаг 2: все выбранные строки, отсортированные по групповым колонкам
        var idParams = new List<string>();
        int idx = 0;
        foreach (var id in selectedIds)
        {
            var pName = $"sid{idx}";
            dp.Add(pName, id);
            idParams.Add($"@{pName}");
            idx++;
        }
        var idFilter     = $"{_idColumnName} IN ({string.Join(",", idParams)})";
        var whereWithIds = ClayDataQuery.CombineWhere(where, idFilter);

        var orderBy = _query.BuildOrderBy(defaultOrder);
        var flatSql = $"SELECT * FROM ({selectSql}) _src";
        if (!string.IsNullOrWhiteSpace(whereWithIds))
            flatSql += $" WHERE {whereWithIds}";
        if (!string.IsNullOrWhiteSpace(orderBy))
            flatSql += $" ORDER BY {orderBy}";

        var items = await Db.QueryAsync<T>(flatSql, dp);

        // Шаг 3: C# interleaving — детектирование смены группового ключа
        var result      = new List<IClayGridRow>();
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

        // Пост-обработка: вычисляем SelectedItemCount для каждого GroupHeaderRow.
        var headerStack = new List<GroupHeaderRow>();
        var countStack  = new List<int>();
        foreach (var row in result)
        {
            if (row is GroupHeaderRow gh)
            {
                while (headerStack.Count > 0 && headerStack[^1].Depth >= gh.Depth)
                {
                    headerStack[^1].SelectedItemCount = countStack[^1];
                    headerStack.RemoveAt(headerStack.Count - 1);
                    countStack.RemoveAt(countStack.Count - 1);
                }
                headerStack.Add(gh);
                countStack.Add(0);
            }
            else if (row is DetailRow<T>)
            {
                for (int i = 0; i < countStack.Count; i++)
                    countStack[i]++;
            }
        }
        while (headerStack.Count > 0)
        {
            headerStack[^1].SelectedItemCount = countStack[^1];
            headerStack.RemoveAt(headerStack.Count - 1);
            countStack.RemoveAt(countStack.Count - 1);
        }

        return result;
    }

    /// <summary>
    /// Возвращает словарь глубина → список FullKey всех групп на этой глубине.
    /// Строится рекурсивным обходом кешированного дерева групп.
    /// </summary>
    private Dictionary<int, List<string>> GetGroupKeysByDepth()
    {
        if (_groupKeysByDepth is not null) return _groupKeysByDepth;
        _groupKeysByDepth = new Dictionary<int, List<string>>();
        if (_groupTreeRoots is not null)
            CollectKeysByDepth(_groupTreeRoots, _groupKeysByDepth);
        return _groupKeysByDepth;
    }

    /// <summary>
    /// Рекурсивно собирает FullKey групп из дерева, группируя их по глубине.
    /// </summary>
    private static void CollectKeysByDepth(
        List<GridGroupNode> nodes, Dictionary<int, List<string>> result)
    {
        foreach (var node in nodes)
        {
            var d = node.Aggregate.Depth;
            if (!result.ContainsKey(d)) result[d] = [];
            result[d].Add(node.Aggregate.FullKey);
            CollectKeysByDepth(node.Children, result);
        }
    }

    /// <summary>
    /// Проверяет, развёрнуты ли ВСЕ группы на указанной глубине.
    /// </summary>
    bool IClayGridDataLoader.IsLevelFullyExpanded(int depth)
    {
        var map = GetGroupKeysByDepth();
        return map.TryGetValue(depth, out var keys) && keys.Count > 0
            && keys.All(k => _query.ExpandedGroups.Contains(k));
    }

    /// <summary>
    /// Переключает состояние ВСЕХ групп на указанной глубине.
    /// При разворачивании — каскадно разворачивает родительские уровни (0..depth-1).
    /// При сворачивании — сворачивает только этот уровень.
    /// Сбрасывает страницу на 1 и перезагружает данные.
    /// </summary>
    async Task IClayGridDataLoader.ToggleLevelExpandedAsync(int depth)
    {
        var map = GetGroupKeysByDepth();
        if (!map.TryGetValue(depth, out var keys) || keys.Count == 0) return;

        bool allExpanded = keys.All(k => _query.ExpandedGroups.Contains(k));

        if (allExpanded)
        {
            foreach (var k in keys) _query.ExpandedGroups.Remove(k);
        }
        else
        {
            for (int d = 0; d <= depth; d++)
                if (map.TryGetValue(d, out var levelKeys))
                    foreach (var k in levelKeys) _query.ExpandedGroups.Add(k);
        }

        _query.PageNumber = 1;
        await LoadData();
    }
}
