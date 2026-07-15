using Clayzor.Lib.Entities.DynamicGrid;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Dapper;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Группировка в динамическом режиме ClayGrid.
/// Переиспользует <see cref="ClayGroupingEngine"/> целиком; отличие от статического режима
/// (<c>ClayGridPageBase.LoadGroupedData</c>) — строки берутся через <see cref="DynamicSql"/>
/// и заворачиваются в <see cref="ClayDynamicRow"/> вместо <c>DetailRow&lt;T&gt;</c>.
/// </summary>
public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>
    /// Раскрытые группы (полные ключи через ) в динамическом режиме.
    /// В статическом режиме тем же владеет ClayGridPageBase._query.ExpandedGroups.
    /// </summary>
    private readonly HashSet<string> _dynamicExpandedGroups = [];

    /// <summary>Корни дерева групп последней загрузки. null — плоский режим или данных нет.</summary>
    private List<GridGroupNode>? _dynamicGroupRoots;

    /// <summary>Кеш: глубина → FullKey всех групп на ней. Сбрасывается при каждой загрузке.</summary>
    private Dictionary<int, List<string>>? _dynamicGroupKeysByDepth;

    private async Task LoadDynamicGroupedData(ClayDataQuery query, string? where, DynamicParameters dp)
    {
        var exprs = query.GroupColumns.ToList();

        // ── 1. Агрегат: одна строка на листовую группу ──────────────────────────
        var groupSql  = ClayGroupingEngine.BuildGroupAggregateSql(SelectSql, exprs, where, query.SortColumns);
        var rawRows   = await DynamicSql.QueryRowsAsync(Db, groupSql, dp);
        var groupRows = ClayDynamicGroupMapper.MapRows(rawRows);

        // ── 2. Дерево групп ────────────────────────────────────────────────────
        var aggregates = ClayGroupingEngine.BuildAggregates(groupRows);
        var roots      = ClayGroupingEngine.BuildTree(aggregates);
        ClayGroupingEngine.ComputeParentCounts(roots);

        _dynamicGroupRoots       = roots;
        _dynamicGroupKeysByDepth = null;   // кеш глубин пересоберётся лениво (GG5)

        // ── 3. Разметка текущей страницы ───────────────────────────────────────
        int totalEffective = roots.Sum(r => ClayGroupingEngine.ComputeEffectiveRows(r, query.ExpandedGroups));
        int pageStart      = (query.PageNumber - 1) * query.PageSize + 1;
        int pageEnd        = query.PageNumber * query.PageSize;
        var layout         = new List<GridLayoutItem>();
        int cur            = 1;
        ClayGroupingEngine.WalkTree(roots, query.ExpandedGroups, pageStart, pageEnd, ref cur, layout);

        // ── 4. Строки: заголовки групп + детали раскрытых групп ────────────────
        var orderBy     = query.BuildOrderBy(DefaultOrder);
        var detailOrder = ClayGroupingEngine.BuildDetailOrder(orderBy, query.GroupColumns, DefaultOrder);
        var newRows     = new List<TEntity>();

        foreach (var item in layout)
        {
            if (item.Header is not null)
                newRows.Add((TEntity)(object)item.Header);

            if (!item.HasDetailRange || item.Aggregate is null) continue;

            var ag           = item.Aggregate;
            var detailParams = new DynamicParameters();
            detailParams.AddDynamicParams(dp);

            var keyParts = ag.RawKeys
                .Select((k, i) => { detailParams.Add($"dk{i}", k); return $"{exprs[i]} = @dk{i}"; })
                .ToList();
            var detailWhere = ClayDataQuery.CombineWhere(where, string.Join(" AND ", keyParts));

            detailParams.Add("__start", item.DetailStart);
            detailParams.Add("__end",   item.DetailEnd);

            var sql  = ClayGroupingEngine.BuildDetailPageSql(SelectSql, detailWhere, detailOrder);
            var rows = await DynamicSql.QueryRowsAsync(Db, sql, detailParams);
            newRows.AddRange(rows.Select(r => (TEntity)(object)new ClayDynamicRow(r)));
        }

        Items      = newRows;
        TotalCount = totalEffective;
    }

    /// <summary>
    /// Раскрывает/сворачивает группу в динамическом режиме.
    /// Копия логики ClayGridPageBase.ToggleGroup, включая автопереход страницы.
    /// </summary>
    private async Task ToggleDynamicGroup(GroupHeaderRow header)
    {
        var wasExpanded = _dynamicExpandedGroups.Contains(header.FullKey);
        if (wasExpanded)
            _dynamicExpandedGroups.Remove(header.FullKey);
        else
            _dynamicExpandedGroups.Add(header.FullKey);

        await NotifyQueryChanged();

        if (!wasExpanded)
        {
            // Раскрыли последнюю группу на странице: её детали физически не влезли —
            // сразу уходим на следующую страницу, иначе клик выглядит как «не сработал».
            var expandedHeader = (Items ?? []).OfType<GroupHeaderRow>()
                .FirstOrDefault(h => h.FullKey == header.FullKey);
            if (expandedHeader is not null)
            {
                var rows      = (Items ?? []).ToList();
                var headerIdx = rows.IndexOf((TEntity)(object)expandedHeader);
                if (headerIdx >= 0 && headerIdx == rows.Count - 1 && header.ItemCount > 0)
                {
                    _pageNumber++;
                    await NotifyQueryChanged();
                }
            }
        }
        else if (TotalCount > 0 && _pageNumber > _totalPages)
        {
            // Свернули группу: эффективных строк стало меньше, текущей страницы может уже не быть.
            _pageNumber = _totalPages;
            await NotifyQueryChanged();
        }

        await InvokeAsync(StateHasChanged);
    }
}
