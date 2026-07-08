using Dapper;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Filter;

/// <summary>
/// Строит фрагмент WHERE (без слова WHERE) из дерева <see cref="ClayFilterGroupNode"/>.
/// </summary>
/// <remarks>
/// Безопасность:
/// <list type="bullet">
///   <item>Имя колонки подставляется в SQL только если оно присутствует в <c>knownColumns</c> (белый список из реестра).
///   Листовой узел с неизвестной колонкой молча отбрасывается.</item>
///   <item>Значения передаются исключительно через Dapper-параметры — без конкатенации в SQL.</item>
///   <item>Имена параметров назначаются сквозным счётчиком (<c>p0, p1, …</c>),
///   что гарантирует уникальность даже при повторном использовании одной колонки в дереве.</item>
/// </list>
/// </remarks>
public static class ClayCompositeSqlBuilder
{
    /// <summary>
    /// Строит фрагмент WHERE из дерева фильтра.
    /// </summary>
    /// <param name="root">Корень дерева фильтра. null или пустое дерево → возвращает null.</param>
    /// <param name="parameters">Dapper-параметры; метод добавляет в них параметры условий.</param>
    /// <param name="knownColumns">
    /// Белый список SQL-имён колонок (из реестра грида).
    /// Листовые узлы с колонкой вне этого множества отбрасываются.
    /// </param>
    /// <param name="columnNameMap">
    /// Необязательный маппинг имён: ключ — SqlName из реестра,
    /// значение — имя для подстановки в SQL (аналог <c>BuildColumnFilterClause</c>).
    /// </param>
    /// <returns>Строка для вставки в WHERE без ключевого слова WHERE, либо null.</returns>
    public static string? Build(
        ClayFilterGroupNode? root,
        DynamicParameters parameters,
        ISet<string> knownColumns,
        IReadOnlyDictionary<string, string>? columnNameMap = null)
    {
        if (root is null) return null;
        int counter = 0;
        return BuildGroup(root, parameters, knownColumns, columnNameMap, ref counter);
    }

    /// <summary>
    /// Рекурсивно строит SQL-фрагмент для группового узла.
    /// Дочерние ненулевые фрагменты объединяются через AND/OR и оборачиваются в скобки.
    /// Если ни один дочерний узел не дал фрагмента — возвращает null.
    /// </summary>
    private static string? BuildGroup(
        ClayFilterGroupNode group,
        DynamicParameters parameters,
        ISet<string> knownColumns,
        IReadOnlyDictionary<string, string>? columnNameMap,
        ref int counter)
    {
        var parts = new List<string>();

        foreach (var node in group.Nodes)
        {
            string? fragment = node switch
            {
                ClayFilterGroupNode nestedGroup
                    => BuildGroup(nestedGroup, parameters, knownColumns, columnNameMap, ref counter),
                ColumnFilter leaf
                    => BuildLeaf(leaf, parameters, knownColumns, columnNameMap, ref counter),
                ValueFilter vf
                    => BuildValueLeaf(vf, parameters, knownColumns, columnNameMap, ref counter),
                _ => null,
            };

            if (fragment is not null)
                parts.Add(fragment);
        }

        if (parts.Count == 0) return null;
        if (parts.Count == 1) return parts[0];

        var logic = group.Logic == LogicalOperator.Or ? "OR" : "AND";
        return "(" + string.Join($" {logic} ", parts) + ")";
    }

    /// <summary>
    /// Строит SQL-фрагмент для листового условия (<see cref="ColumnFilter"/>).
    /// Колонка проверяется по белому списку; неизвестные колонки → null.
    /// Имена параметров назначаются через сквозной счётчик.
    /// </summary>
    private static string? BuildLeaf(
        ColumnFilter cf,
        DynamicParameters parameters,
        ISet<string> knownColumns,
        IReadOnlyDictionary<string, string>? columnNameMap,
        ref int counter)
    {
        // Белый список: колонка должна быть зарегистрирована в гриде
        if (!knownColumns.Contains(cf.Column))
            return null;

        if (!cf.HasValue)
            return null;

        // Применяем маппинг имени если задан
        var colName = columnNameMap is not null && columnNameMap.TryGetValue(cf.Column, out var mapped)
            ? mapped
            : cf.Column;

        // Первое условие — уникальное имя параметра через счётчик
        var p1 = $"p{counter++}";
        var clause1 = ClayDataQuery.BuildSingleClause(colName, p1, cf.Operator, cf.Value, parameters);
        if (clause1 is null) return null;

        if (!cf.HasSecondClause)
            return clause1;

        // Второе условие
        var p2 = $"p{counter++}";
        var clause2 = ClayDataQuery.BuildSingleClause(colName, p2, cf.SecondOperator, cf.SecondValue, parameters);
        if (clause2 is null)
            return clause1;

        var logic = cf.LogicalOperator == LogicalOperator.Or ? "OR" : "AND";
        return $"({clause1} {logic} {clause2})";
    }

    /// <summary>
    /// Строит SQL-фрагмент для листового узла фильтрации по набору значений
    /// (<see cref="ValueFilter"/>). Генерирует <c>IN (...)</c> или
    /// <c>NOT IN (...)</c> в зависимости от <see cref="ValueFilter.Negate"/>,
    /// с учётом <see cref="ValueFilter.BlankChecked"/> для включения NULL
    /// и пустых строк.
    /// Колонка проверяется по белому списку; неизвестные колонки → null.
    /// Имена параметров назначаются через сквозной счётчик.
    /// </summary>
    private static string? BuildValueLeaf(
        ValueFilter vf,
        DynamicParameters parameters,
        ISet<string> knownColumns,
        IReadOnlyDictionary<string, string>? columnNameMap,
        ref int counter)
    {
        // Белый список: колонка должна быть зарегистрирована в гриде
        if (!knownColumns.Contains(vf.Column))
            return null;

        if (!vf.HasValue)
            return null;

        // Применяем маппинг имени если задан
        var colName = columnNameMap is not null && columnNameMap.TryGetValue(vf.Column, out var mapped)
            ? mapped
            : vf.Column;

        var hasValues = vf.Values.Count > 0;
        var negate = vf.Negate;
        var blank  = vf.BlankChecked;

        // Есть ли строковые значения — для обработки пустых строк ''
        var hasStringValues = vf.Values.Any(v => v is string);

        string? valueClause = null;
        if (hasValues)
        {
            var paramNames = new List<string>();
            foreach (var value in vf.Values)
            {
                var pName = $"p{counter++}";
                parameters.Add(pName, value);
                paramNames.Add($"@{pName}");
            }

            var list = string.Join(", ", paramNames);
            valueClause = negate
                ? $"{colName} NOT IN ({list})"
                : $"{colName} IN ({list})";
        }

        // Сборка финального фрагмента
        var parts = new List<string>();

        if (valueClause is not null)
            parts.Add(valueClause);

        if (blank)
        {
            parts.Add($"{colName} IS NULL");
            if (hasStringValues)
                parts.Add($"{colName} = ''");
        }
        else if (negate)
        {
            // Negate=true, BlankChecked=false — явно исключаем NULL/пустые строки
            parts.Add($"{colName} IS NOT NULL");
            if (hasStringValues)
                parts.Add($"{colName} <> ''");
        }

        if (parts.Count == 0)
            return null; // Negate=true, BlankChecked=true, Values пуст → 1=1

        // Логика объединения:
        // BlankChecked=true → OR  (добавляем пустые строки к выборке)
        // BlankChecked=false → AND (сужаем: исключаем и значения, и пустые)
        var separator = blank ? " OR " : " AND ";
        return $"({string.Join(separator, parts)})";
    }
}
