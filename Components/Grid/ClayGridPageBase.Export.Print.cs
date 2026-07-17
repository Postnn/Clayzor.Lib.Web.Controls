using Dapper;
using Clayzor.Lib.Entities;
using Clayzor.Lib.Web.Controls.Components.Grid.Filter;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public abstract partial class ClayGridPageBase<T> where T : Entity
{
    /// <summary>
    /// Плоский режим: все строки без ROW_NUMBER() и пагинации.
    /// Используется печатью (все данные) и Excel — раскрытость групп игнорируется,
    /// выгружается весь список.
    /// </summary>
    private async Task<List<IClayGridRow>> BuildAllFlatRowsForExport()
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
}
