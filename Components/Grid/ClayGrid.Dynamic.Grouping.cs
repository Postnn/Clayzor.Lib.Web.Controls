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

    /// <summary>WHERE последней групповой загрузки — для ленивой догрузки ID потомков групп.</summary>
    private string? _dynamicGroupWhere;

    /// <summary>Параметры последней групповой загрузки (@search + фильтр).</summary>
    private DynamicParameters? _dynamicGroupParams;

    /// <summary>Выражения группировки последней загрузки.</summary>
    private List<string> _dynamicGroupExprs = [];

    private async Task LoadDynamicGroupedData(ClayDataQuery query, string? where, DynamicParameters dp)
    {
        var exprs = query.GroupColumns.ToList();

        _dynamicGroupWhere  = where;
        _dynamicGroupParams = dp;
        _dynamicGroupExprs  = exprs;

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

        // Подмена кода на наименование в заголовках групп (Тип 5/9).
        // Только DisplayValue: FullKey, KeyValues и RawKeys обязаны остаться кодами —
        // на них построены ExpandedGroups и WHERE детального запроса.
        foreach (var item in layout)
        {
            if (item.Header is null) continue;
            var depth = item.Header.Depth;
            if (depth < 0 || depth >= exprs.Count) continue;
            item.Header.DisplayValue = ResolveGroupDisplayValue(exprs[depth], item.Header.DisplayValue);
        }

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

    /// <summary>
    /// Словарь глубина → FullKey всех групп на этой глубине. Строится лениво по дереву
    /// последней загрузки; кеш сбрасывается в LoadDynamicGroupedData / LoadDynamicFlatData.
    /// </summary>
    private Dictionary<int, List<string>> GetDynamicGroupKeysByDepth()
    {
        if (_dynamicGroupKeysByDepth is not null) return _dynamicGroupKeysByDepth;
        _dynamicGroupKeysByDepth = new Dictionary<int, List<string>>();
        if (_dynamicGroupRoots is not null)
            CollectDynamicKeysByDepth(_dynamicGroupRoots, _dynamicGroupKeysByDepth);
        return _dynamicGroupKeysByDepth;
    }

    /// <summary>Рекурсивно собирает FullKey групп из дерева, раскладывая по глубине.</summary>
    private static void CollectDynamicKeysByDepth(
        List<GridGroupNode> nodes, Dictionary<int, List<string>> result)
    {
        foreach (var node in nodes)
        {
            var d = node.Aggregate.Depth;
            if (!result.ContainsKey(d)) result[d] = [];
            result[d].Add(node.Aggregate.FullKey);
            CollectDynamicKeysByDepth(node.Children, result);
        }
    }

    /// <summary>Развёрнуты ли ВСЕ группы на указанной глубине (динамический режим).</summary>
    private bool IsDynamicLevelFullyExpanded(int depth)
    {
        var map = GetDynamicGroupKeysByDepth();
        return map.TryGetValue(depth, out var keys) && keys.Count > 0
            && keys.All(k => _dynamicExpandedGroups.Contains(k));
    }

    /// <summary>
    /// Переключает ВСЕ группы на указанной глубине (динамический режим).
    /// Разворачивание каскадно раскрывает родительские уровни 0..depth-1, иначе
    /// раскрытые внутренние группы просто не будут видны под свёрнутым родителем.
    /// Сворачивание трогает только этот уровень.
    /// </summary>
    private async Task ToggleDynamicLevelExpanded(int depth)
    {
        var map = GetDynamicGroupKeysByDepth();
        if (!map.TryGetValue(depth, out var keys) || keys.Count == 0) return;

        bool allExpanded = keys.All(k => _dynamicExpandedGroups.Contains(k));

        if (allExpanded)
        {
            foreach (var k in keys) _dynamicExpandedGroups.Remove(k);
        }
        else
        {
            for (int d = 0; d <= depth; d++)
                if (map.TryGetValue(d, out var levelKeys))
                    foreach (var k in levelKeys) _dynamicExpandedGroups.Add(k);
        }

        _pageNumber = 1;
        await NotifyQueryChanged();
    }

    /// <summary>
    /// Загружает ID всех строк указанных групп (динамический режим).
    /// Аналог ClayGridPageBase.LoadGroupChildIdsAsync: тот же SQL, но SelectSql и колонка Id
    /// берутся из определения грида, а запрос идёт через DynamicSql.
    /// Строки, чей Id не приводится к int, пропускаются (см. TryGetSelectionId в GF13).
    /// </summary>
    private async Task<Dictionary<string, HashSet<int>>> LoadDynamicGroupChildIdsAsync(
        IReadOnlyList<string> groupFullKeys)
    {
        var result = new Dictionary<string, HashSet<int>>();

        var idColumn = _dynamicDef?.IdColumn;
        if (groupFullKeys.Count == 0
            || string.IsNullOrWhiteSpace(idColumn)
            || _dynamicGroupParams is null
            || _dynamicGroupExprs.Count == 0)
            return result;

        // Белый список: IdColumn приходит из справочника Запросы и подставляется в SQL текстом.
        if (!_dynamicKnownColumns.Contains(idColumn))
            return result;

        foreach (var fullKey in groupFullKeys)
        {
            var keys     = fullKey.Split('');
            var dp       = new DynamicParameters();
            dp.AddDynamicParams(_dynamicGroupParams);
            var keyParts = new List<string>();

            for (int i = 0; i < keys.Length && i < _dynamicGroupExprs.Count; i++)
            {
                var pName = $"gk_{fullKey.GetHashCode() & 0x7FFFFFFF}_{i}";
                dp.Add(pName, keys[i]);
                keyParts.Add($"{_dynamicGroupExprs[i]} = @{pName}");
            }

            if (keyParts.Count == 0) continue;

            var groupWhere    = string.Join(" AND ", keyParts);
            var combinedWhere = ClayDataQuery.CombineWhere(_dynamicGroupWhere, groupWhere);

            var sql = $"SELECT {idColumn} FROM ({SelectSql}) _src";
            if (!string.IsNullOrWhiteSpace(combinedWhere))
                sql += $" WHERE {combinedWhere}";

            var rows = await DynamicSql.QueryRowsAsync(Db, sql, dp);

            var ids = new HashSet<int>();
            foreach (var row in rows)
            {
                var raw = row.GetValueOrDefault(idColumn);
                if (raw is null or DBNull) continue;
                if (int.TryParse(raw.ToString(), out var id))
                    ids.Add(id);
            }

            result[fullKey] = ids;
        }

        return result;
    }

    /// <summary>
    /// Сбрасывает раскрытые группы. Вызывается при изменении состава/порядка группировки:
    /// ключи групп (FullKey) строятся из значений группировочных колонок, при другой
    /// группировке они бессмысленны и будут молча висеть в наборе.
    /// </summary>
    private void ResetDynamicExpandedGroups() => _dynamicExpandedGroups.Clear();

    /// <summary>
    /// Отображаемое значение группы. Группировка идёт по коду (значению колонки в SQL),
    /// а показывать нужно наименование — как в ячейке.
    /// Тип 5 (Список): _dynamicLookups[колонка][код] → наименование.
    /// Тип 9 (Пиктограмма): _dynamicIconLookups[колонка][код].Tooltip — картинку в текстовом
    /// заголовке группы не показать, тултип это человекочитаемая подпись значения.
    /// Кода нет в справочнике → возвращаем код как есть (так же ведёт себя cell-шаблон).
    /// </summary>
    private string ResolveGroupDisplayValue(string groupSqlName, string rawValue)
    {
        if (_dynamicLookups.TryGetValue(groupSqlName, out var lookup)
            && lookup.TryGetValue(rawValue, out var text))
            return text;

        if (_dynamicIconLookups.TryGetValue(groupSqlName, out var iconLookup)
            && iconLookup.TryGetValue(rawValue, out var iconData))
            return iconData.Tooltip;

        return rawValue;
    }
}
