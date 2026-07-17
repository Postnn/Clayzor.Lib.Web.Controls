using Clayzor.Lib.Entities;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

// ── Вспомогательные типы серверной группировки ─────────────────────────────

/// <summary>
/// Строка результата агрегатного GROUP BY-запроса.
/// Число уровней не ограничено: <see cref="Keys"/> содержит ровно столько значений,
/// сколько колонок в группировке, в том же порядке.
/// </summary>
public class GridGroupRow
{
    /// <summary>
    /// Значения группировочных колонок по уровням: Keys[0] — внешний уровень.
    /// null — законное значение ключа (NULL в данных), а НЕ признак отсутствия уровня.
    /// </summary>
    public List<object?> Keys { get; set; } = [];

    /// <summary>Количество строк детализации в этой листовой группе.</summary>
    public int Cnt { get; set; }
}

/// <summary>
/// Групповой агрегат — узел с метаданными группы (ключ, глубина, количество).
/// Синтетические родительские узлы создаются для промежуточных уровней многоуровневой группировки.
/// </summary>
public class GridGroupAgg
{
    /// <summary>Полный ключ группы — уровни через .</summary>
    public string FullKey { get; set; } = "";

    /// <summary>Отображаемое значение группы (значение последнего уровня).</summary>
    public string DisplayValue { get; set; } = "";

    /// <summary>Количество строк детализации. Для синтетических родителей вычисляется через <see cref="ClayGroupingEngine.ComputeParentCounts"/>.</summary>
    public int ItemCount { get; set; }

    /// <summary>Уровень вложенности: 0 — внешний.</summary>
    public int Depth { get; set; }

    /// <summary>Полный ключ родительского агрегата. Пустая строка — корневой узел.</summary>
    public string ParentKey { get; set; } = "";

    /// <summary>Строковые значения ключей по уровням.</summary>
    public List<string> KeyValues { get; set; } = [];

    /// <summary>Исходные (нетипизированные) значения ключей для параметров WHERE детального запроса.</summary>
    public List<object?> RawKeys { get; set; } = [];
}

/// <summary>
/// Узел дерева групп. Содержит агрегат, дочерние узлы и вычисленное эффективное число строк.
/// </summary>
public class GridGroupNode
{
    /// <summary>Агрегат этого узла.</summary>
    public GridGroupAgg Aggregate { get; set; } = default!;

    /// <summary>Дочерние узлы (подгруппы следующего уровня).</summary>
    public List<GridGroupNode> Children { get; set; } = [];

    /// <summary>
    /// Эффективное число строк — сколько строк занимает этот узел на странице
    /// с учётом своей строки-заголовка и раскрытых дочерних строк.
    /// Вычисляется в <see cref="ClayGroupingEngine.ComputeEffectiveRows"/>.
    /// </summary>
    public int EffectiveRows { get; set; }
}

/// <summary>
/// Элемент макета страницы: заголовок группы + опциональный диапазон детальных строк.
/// Формируется при обходе дерева в <see cref="ClayGroupingEngine.WalkTree"/>.
/// </summary>
public class GridLayoutItem
{
    /// <summary>Заголовок группы для рендеринга.</summary>
    public GroupHeaderRow? Header { get; set; }

    /// <summary>Агрегат этого узла (нужен для построения детального WHERE).</summary>
    public GridGroupAgg? Aggregate { get; set; }

    /// <summary>Начало диапазона детальных строк внутри группы (1-based).</summary>
    public int DetailStart { get; set; }

    /// <summary>Конец диапазона детальных строк внутри группы (1-based).</summary>
    public int DetailEnd { get; set; }

    /// <summary>Есть ли детальные строки для загрузки на текущей странице.</summary>
    public bool HasDetailRange { get; set; }
}

// ── Движок группировки ──────────────────────────────────────────────────────

/// <summary>
/// Статический движок серверной группировки для <see cref="ClayGrid{TEntity}"/>.
/// Строит SQL, преобразует плоские результаты GROUP BY в дерево,
/// вычисляет страничную разметку.
/// Не зависит от Blazor, MudBlazor или DbManager.
/// </summary>
public static class ClayGroupingEngine
{
    /// <summary>
    /// Строит агрегатный SQL-запрос GROUP BY (SQL Server 2008 R2 совместимый).
    /// Возвращает по одной ключевой колонке K{i} на каждый уровень группировки + COUNT(*) AS Cnt.
    /// Число уровней не ограничено — сколько колонок передано, столько и будет.
    /// </summary>
    /// <param name="selectSql">Базовый SELECT без WHERE/ORDER BY.</param>
    /// <param name="groupExprs">Выходные имена колонок группировки в порядке приоритета. Не пустой.</param>
    /// <param name="where">WHERE-фрагмент или null.</param>
    /// <param name="sortColumns">Текущая сортировка — определяет ORDER BY агрегата.</param>
    /// <exception cref="ArgumentException">groupExprs пуст — вызывающий обязан это проверять.</exception>
    public static string BuildGroupAggregateSql(
        string selectSql,
        IReadOnlyList<string> groupExprs,
        string? where,
        IReadOnlyList<SortColumn> sortColumns)
    {
        if (groupExprs.Count == 0)
            throw new ArgumentException("Список колонок группировки пуст.", nameof(groupExprs));

        var selectParts = groupExprs.Select((expr, i) => $"{expr} AS K{i}");

        var grp = string.Join(", ", groupExprs);
        var ordParts = groupExprs.Select(expr =>
        {
            var sc = sortColumns.FirstOrDefault(s => s.Column == expr);
            return sc is not null ? $"{expr} {(sc.Desc ? "DESC" : "ASC")}" : expr;
        });

        var sql = $"SELECT {string.Join(", ", selectParts)}, COUNT(*) AS Cnt";
        sql += $" FROM ({selectSql}) _g";
        if (where is not null)
            sql += $" WHERE {where}";
        sql += $" GROUP BY {grp}";
        sql += $" ORDER BY {string.Join(", ", ordParts)}";
        return sql;
    }

    /// <summary>
    /// Строит постраничный детальный SQL с <c>ROW_NUMBER()</c> (SQL Server 2008 R2).
    /// Параметры границ страницы: <c>@__start</c> и <c>@__end</c>.
    /// </summary>
    /// <param name="selectSql">Базовый SELECT.</param>
    /// <param name="where">WHERE-фрагмент или null.</param>
    /// <param name="detailOrder">ORDER BY для строк внутри группы.</param>
    public static string BuildDetailPageSql(
        string selectSql,
        string? where,
        string detailOrder)
    {
        var sql = $"SELECT * FROM (SELECT _src.*, ROW_NUMBER() OVER (ORDER BY {detailOrder}) AS _drn";
        sql += $" FROM ({selectSql}) _src";
        if (!string.IsNullOrWhiteSpace(where))
            sql += $" WHERE {where}";
        sql += ") _d WHERE _drn BETWEEN @__start AND @__end";
        return sql;
    }

    /// <summary>
    /// Строит ORDER BY для детальных строк: исключает колонки группировки,
    /// чтобы сортировка внутри группы не дублировала GROUP BY.
    /// </summary>
    /// <param name="fullOrderBy">Полный ORDER BY из <see cref="ClayDataQuery.BuildOrderBy"/>.</param>
    /// <param name="groupColumns">SQL-имена колонок группировки.</param>
    /// <param name="fallbackOrder">Порядок по умолчанию, если после исключения не осталось колонок.</param>
    public static string BuildDetailOrder(
        string fullOrderBy,
        IEnumerable<string> groupColumns,
        string fallbackOrder)
    {
        var groupSet = new HashSet<string>(groupColumns);
        var parts = fullOrderBy
            .Split(", ", StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !groupSet.Contains(p.Split(' ')[0]))
            .ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : fallbackOrder;
    }

    /// <summary>Отображаемое значение группы, когда ключ уровня равен NULL.</summary>
    public const string EmptyGroupDisplay = "(пусто)";

    /// <summary>
    /// Превращает плоские строки GROUP BY в список <see cref="GridGroupAgg"/>
    /// с синтетическими родительскими узлами для промежуточных уровней.
    /// Число уровней берётся из <see cref="GridGroupRow.Keys"/> и не ограничено.
    /// Порядок агрегатов из БД сохраняется (не пересортировывается): <see cref="BuildTree"/>
    /// требует, чтобы родитель предшествовал ребёнку.
    /// </summary>
    public static List<GridGroupAgg> BuildAggregates(IEnumerable<GridGroupRow> groupRows)
    {
        var aggregates = new List<GridGroupAgg>();
        var seenKeys   = new HashSet<string>();

        foreach (var gr in groupRows)
        {
            if (gr.Keys.Count == 0) continue;   // защита от пустой строки агрегата

            // Строковые представления ключей ВСЕХ уровней. null → "" (законное значение).
            var keys  = gr.Keys.Select(k => k?.ToString() ?? "").ToList();
            var depth = keys.Count - 1;

            // Синтетические родители для всех промежуточных уровней 0..depth-1
            for (int d = 0; d < depth; d++)
            {
                var parentKeys    = keys.Take(d + 1).ToList();
                var parentFullKey = string.Join("", parentKeys);
                if (!seenKeys.Add(parentFullKey)) continue;

                aggregates.Add(new GridGroupAgg
                {
                    FullKey      = parentFullKey,
                    DisplayValue = ToDisplay(parentKeys[d]),
                    ItemCount    = 0,                       // посчитает ComputeParentCounts
                    Depth        = d,
                    ParentKey    = d > 0 ? string.Join("", parentKeys.Take(d)) : "",
                    KeyValues    = parentKeys,
                    RawKeys      = gr.Keys.Take(d + 1).ToList(),
                });
            }

            var fullKey   = string.Join("", keys);
            var parentKey = depth > 0 ? string.Join("", keys.Take(depth)) : "";

            aggregates.Add(new GridGroupAgg
            {
                FullKey      = fullKey,
                DisplayValue = ToDisplay(keys[depth]),
                ItemCount    = gr.Cnt,
                Depth        = depth,
                ParentKey    = parentKey,
                KeyValues    = keys,
                RawKeys      = gr.Keys.ToList(),
            });
        }

        return aggregates;
    }

    /// <summary>Строковый ключ → подпись группы. Пустой ключ (NULL в данных) → «(пусто)».</summary>
    private static string ToDisplay(string key) => key.Length > 0 ? key : EmptyGroupDisplay;

    /// <summary>
    /// Строит WHERE-фрагмент, отбирающий строки одной группы по её ключам.
    /// NULL-ключ превращается в <c>IS NULL</c>: <c>col = @p</c> при NULL-значении параметра
    /// не истинно никогда, и группа «(пусто)» раскрывалась бы пустой.
    /// Число уровней не ограничено — берётся из <paramref name="rawKeys"/>.
    /// Значения ключей уходят ПАРАМЕТРАМИ; в текст SQL подставляются только выражения колонок
    /// из <paramref name="groupExprs"/> (они обязаны быть провалидированы вызывающим).
    /// </summary>
    /// <param name="groupExprs">Выражения колонок группировки по уровням.</param>
    /// <param name="rawKeys">Сырые значения ключей группы (<see cref="GridGroupAgg.RawKeys"/>).</param>
    /// <param name="paramPrefix">Префикс имён параметров, напр. "dk". Должен быть уникален в пределах одного набора параметров.</param>
    /// <param name="parameters">Пары имя-значение для добавления в DynamicParameters. Для NULL-ключей параметр не создаётся.</param>
    /// <returns>Фрагмент вида <c>a = @dk0 AND b IS NULL</c>. Пустая строка, если ключей нет.</returns>
    public static string BuildGroupKeyWhere(
        IReadOnlyList<string> groupExprs,
        IReadOnlyList<object?> rawKeys,
        string paramPrefix,
        out List<(string Name, object? Value)> parameters)
    {
        parameters = [];
        if (rawKeys.Count == 0) return "";

        var parts = new List<string>(rawKeys.Count);
        for (int i = 0; i < rawKeys.Count && i < groupExprs.Count; i++)
        {
            if (rawKeys[i] is null)
            {
                parts.Add($"{groupExprs[i]} IS NULL");
                continue;
            }

            var name = $"{paramPrefix}{i}";
            parameters.Add((name, rawKeys[i]));
            parts.Add($"{groupExprs[i]} = @{name}");
        }

        return string.Join(" AND ", parts);
    }

    /// <summary>
    /// Строит дерево <see cref="GridGroupNode"/> из плоского списка агрегатов.
    /// Предполагает, что родительский агрегат в списке всегда предшествует дочернему.
    /// </summary>
    public static List<GridGroupNode> BuildTree(List<GridGroupAgg> aggregates)
    {
        var roots  = new List<GridGroupNode>();
        var lookup = new Dictionary<string, GridGroupNode>();

        foreach (var a in aggregates)
        {
            var node = new GridGroupNode { Aggregate = a, Children = [] };
            lookup[a.FullKey] = node;
            if (string.IsNullOrEmpty(a.ParentKey))
                roots.Add(node);
            else if (lookup.TryGetValue(a.ParentKey, out var parent))
                parent.Children.Add(node);
        }

        return roots;
    }

    /// <summary>
    /// Рекурсивно вычисляет <see cref="GridGroupAgg.ItemCount"/> родительских (синтетических)
    /// узлов как сумму ItemCount всех дочерних листьев.
    /// </summary>
    /// <returns>Суммарное количество строк детализации.</returns>
    public static int ComputeParentCounts(List<GridGroupNode> roots)
    {
        int total = 0;
        foreach (var node in roots)
        {
            if (node.Children.Count > 0)
            {
                int childSum = ComputeParentCounts(node.Children);
                node.Aggregate.ItemCount = childSum;
                total += childSum;
            }
            else
            {
                total += node.Aggregate.ItemCount;
            }
        }
        return total;
    }

    /// <summary>
    /// Рекурсивно вычисляет <see cref="GridGroupNode.EffectiveRows"/>:
    /// сколько строк занимает узел на странице с учётом раскрытых групп.
    /// </summary>
    public static int ComputeEffectiveRows(GridGroupNode node, HashSet<string> expanded)
    {
        int rows = 1; // сама строка-заголовок
        if (expanded.Contains(node.Aggregate.FullKey))
        {
            foreach (var child in node.Children)
                rows += ComputeEffectiveRows(child, expanded);
            if (node.Children.Count == 0)
                rows += node.Aggregate.ItemCount;
        }
        node.EffectiveRows = rows;
        return rows;
    }

    /// <summary>
    /// Обходит дерево групп и формирует плоский список <see cref="GridLayoutItem"/>
    /// для строк, попадающих в диапазон текущей страницы [pageStart, pageEnd].
    /// </summary>
    public static void WalkTree(
        List<GridGroupNode> nodes,
        HashSet<string> expanded,
        int pageStart, int pageEnd,
        ref int currentRow,
        List<GridLayoutItem> layout)
    {
        foreach (var node in nodes)
        {
            int groupStart = currentRow;
            int groupEnd   = currentRow + node.EffectiveRows - 1;

            if (groupStart > pageEnd)
            {
                currentRow = groupEnd + 1;
                return;
            }

            bool overlaps   = groupEnd >= pageStart && groupStart <= pageEnd;
            bool isExpanded = expanded.Contains(node.Aggregate.FullKey);

            if (overlaps)
            {
                layout.Add(new GridLayoutItem
                {
                    Header = new GroupHeaderRow
                    {
                        DisplayValue = node.Aggregate.DisplayValue,
                        FullKey      = node.Aggregate.FullKey,
                        ItemCount    = node.Aggregate.ItemCount,
                        Depth        = node.Aggregate.Depth,
                        IsExpanded   = isExpanded,
                        GroupKeys    = node.Aggregate.KeyValues,
                    },
                    Aggregate = node.Aggregate,
                });
            }
            currentRow++;

            if (isExpanded)
            {
                WalkTree(node.Children, expanded, pageStart, pageEnd, ref currentRow, layout);
                if (currentRow > pageEnd)
                {
                    currentRow = groupEnd + 1;
                    return;
                }

                if (node.Children.Count == 0)
                {
                    int detailCount = node.Aggregate.ItemCount;
                    if (overlaps && detailCount > 0)
                    {
                        int firstDetail = Math.Max(1, pageStart - currentRow + 1);
                        int lastDetail  = Math.Min(detailCount, pageEnd - currentRow + 1);
                        if (firstDetail <= lastDetail)
                        {
                            var last = layout.Last();
                            last.DetailStart    = firstDetail;
                            last.DetailEnd      = lastDetail;
                            last.HasDetailRange = true;
                        }
                    }
                    currentRow += detailCount;
                }
            }
            else
            {
                currentRow = groupEnd + 1;
            }
        }
    }
}
