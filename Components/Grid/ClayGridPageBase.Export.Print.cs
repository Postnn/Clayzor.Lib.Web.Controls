using Dapper;
using Clayzor.Lib.Entities;
using Clayzor.Lib.Web.Controls.Components.Grid.Filter;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public abstract partial class ClayGridPageBase<T> where T : Entity
{
    /// <summary>
    /// Загружает ВСЕ строки без пагинации, соответствующие текущему запросу
    /// (поиск + фильтры), с учётом группировки и состояния развёрнутости групп.
    /// НЕ модифицирует <see cref="_rows"/> — возвращает новый список.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildAllRowsForPrint()
    {
        if (_query.GroupEnabled && _query.GroupColumns.Count > 0)
            return await BuildAllGroupedRowsForPrint();
        return await BuildAllFlatRowsForPrint();
    }

    /// <summary>
    /// Плоский режим: все строки без ROW_NUMBER() и пагинации.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildAllFlatRowsForPrint()
    {
        var selectSql     = Grid?.SelectSql     ?? string.Empty;
        var searchColumns = Grid?.SearchColumns ?? [];
        var defaultOrder  = Grid?.DefaultOrder  ?? string.Empty;

        var searchWhere    = _query.BuildWhereClause(searchColumns);
        var orderBy        = _query.BuildOrderBy(defaultOrder);
        var dp             = new DynamicParameters();
        dp.Add("search", $"%{_query.SearchText}%");
        var compositeWhere = BuildCompositeFilterClause(_query.CompositeFilter, dp);
        var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);

        var sql = $"SELECT * FROM ({selectSql}) _src";
        if (!string.IsNullOrWhiteSpace(where))
            sql += $" WHERE {where}";
        if (!string.IsNullOrWhiteSpace(orderBy))
            sql += $" ORDER BY {orderBy}";

        var items = await Db.QueryAsync<T>(sql, dp);
        return items.Select(i => (IClayGridRow)new DetailRow<T> { Item = i }).ToList();
    }

    /// <summary>
    /// Режим группировки: GROUP BY для всего дерева, WalkTree без страничных
    /// границ (pageStart=1, pageEnd=int.MaxValue), все detail-строки развёрнутых групп.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildAllGroupedRowsForPrint()
    {
        var selectSql     = Grid?.SelectSql     ?? string.Empty;
        var searchColumns = Grid?.SearchColumns ?? [];
        var defaultOrder  = Grid?.DefaultOrder  ?? string.Empty;

        var searchWhere    = _query.BuildWhereClause(searchColumns);
        var orderBy        = _query.BuildOrderBy(defaultOrder);
        var dp             = new DynamicParameters();
        dp.Add("search", $"%{_query.SearchText}%");
        var compositeWhere = BuildCompositeFilterClause(_query.CompositeFilter, dp);
        var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);

        var exprs = _query.GroupColumns.ToList();

        var groupSql  = ClayGroupingEngine.BuildGroupAggregateSql(selectSql, exprs, where, _query.SortColumns);
        var groupRows = await Db.QueryAsync<GridGroupRow>(groupSql, dp);

        var aggregates = ClayGroupingEngine.BuildAggregates(groupRows);
        var roots      = ClayGroupingEngine.BuildTree(aggregates);
        ClayGroupingEngine.ComputeParentCounts(roots);
        foreach (var r in roots)
            ClayGroupingEngine.ComputeEffectiveRows(r, _query.ExpandedGroups);

        var layout = new List<GridLayoutItem>();
        int cur    = 1;
        ClayGroupingEngine.WalkTree(roots, _query.ExpandedGroups, 1, int.MaxValue, ref cur, layout);

        var result      = new List<IClayGridRow>();
        var detailOrder = ClayGroupingEngine.BuildDetailOrder(orderBy, _query.GroupColumns, defaultOrder);

        foreach (var item in layout)
        {
            if (item.Header is not null)
                result.Add(item.Header);

            if (item.HasDetailRange && item.Aggregate is not null)
            {
                var ag           = item.Aggregate;
                var detailParams = new DynamicParameters();
                detailParams.AddDynamicParams(dp);

                var keyParts = ag.RawKeys
                    .Select((k, i) => { detailParams.Add($"dk{i}", k); return $"{exprs[i]} = @dk{i}"; })
                    .ToList();
                var detailWhere = ClayDataQuery.CombineWhere(where, string.Join(" AND ", keyParts));

                detailParams.Add("__start", item.DetailStart);
                detailParams.Add("__end",   item.DetailEnd);

                var sql  = ClayGroupingEngine.BuildDetailPageSql(selectSql, detailWhere, detailOrder);
                var rows = await Db.QueryAsync<T>(sql, detailParams);
                result.AddRange(rows.Select(i => new DetailRow<T> { Item = i, Depth = ag.Depth }));
            }
        }

        return result;
    }
}
