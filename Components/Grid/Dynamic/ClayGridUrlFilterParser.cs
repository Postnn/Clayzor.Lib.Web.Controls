using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;
using Clayzor.Lib.Web.Controls.Components.Grid.Filter;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Результат разбора одного URL-параметра фильтра.
/// </summary>
/// <param name="UrlKey">Ключ колонки (UrlKey из определения, без ведущего '_').</param>
/// <param name="Operator">Оператор фильтрации.</param>
/// <param name="Value">Значение фильтра.</param>
/// <param name="IsDefault">Правило 1: параметр с префиксом '_' — применить только если нет сохранённого.</param>
/// <param name="IsForced">Правило 2: параметр без '_' — применить всегда, не сохранять.</param>
public sealed record ParsedUrlFilter(
    string UrlKey,
    ColumnFilterOperator Operator,
    string Value,
    bool IsDefault,
    bool IsForced);

/// <summary>
/// Разбор URL-фильтра <c>КлючURL=op~value</c> и слияние в дерево фильтра.
/// ЧИСТАЯ логика — без зависимостей от БД/URL API.
/// </summary>
public static class ClayGridUrlFilterParser
{
    private static readonly Dictionary<string, ColumnFilterOperator> _operatorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"]       = ColumnFilterOperator.Equals,
        ["ne"]       = ColumnFilterOperator.NotEquals,
        ["gt"]       = ColumnFilterOperator.GreaterThan,
        ["ge"]       = ColumnFilterOperator.GreaterThanOrEqual,
        ["lt"]       = ColumnFilterOperator.LessThan,
        ["lte"]      = ColumnFilterOperator.LessThanOrEqual,
        ["contains"] = ColumnFilterOperator.Contains,
        ["ncontains"]= ColumnFilterOperator.NotContains,
        ["startswith"] = ColumnFilterOperator.StartsWith,
        ["nstartswith"]= ColumnFilterOperator.NotStartsWith,
        ["endswith"]  = ColumnFilterOperator.EndsWith,
        ["nendswith"] = ColumnFilterOperator.NotEndsWith,
        ["in"]        = ColumnFilterOperator.Equals,
        ["between"]   = ColumnFilterOperator.Equals,
    };

    /// <summary>
    /// Разбирает ОДИН URL-параметр фильтра.
    /// </summary>
    /// <param name="rawParamName">Сырое имя параметра: "_name" или "name".</param>
    /// <param name="rawValue">Сырое значение: "eq~DQA1" или "20260101".</param>
    /// <param name="col">Дескриптор типа колонки (для дефолтного оператора).</param>
    public static ParsedUrlFilter Parse(string rawParamName, string rawValue, ColumnTypeDescriptor col)
    {
        var isDefault = rawParamName.StartsWith('_');
        var urlKey    = isDefault ? rawParamName[1..] : rawParamName;

        // Правило 5: ищем "op~" — если нет, дефолтный оператор + rawValue целиком
        var tildeIdx = rawValue.IndexOf('~');
        ColumnFilterOperator op;
        string value;

        if (tildeIdx > 0 && _operatorMap.TryGetValue(rawValue[..tildeIdx], out var parsedOp))
        {
            op    = parsedOp;
            value = rawValue[(tildeIdx + 1)..];
        }
        else
        {
            op    = col.DefaultOperator;
            value = rawValue;
        }

        return new ParsedUrlFilter(urlKey, op, value, isDefault, !isDefault);
    }

    /// <summary>
    /// Применяет разобранные URL-фильтры к дереву фильтра.
    /// Правило 1 (IsDefault): только если ключа нет в <paramref name="savedUserParams"/>.
    /// Правило 2 (IsForced): применяется всегда.
    /// </summary>
    public static void Apply(
        ClayFilterGroupNode root,
        IEnumerable<ParsedUrlFilter> parsed,
        IReadOnlyDictionary<string, string> savedUserParams)
    {
        foreach (var pf in parsed)
        {
            // Правило 1: дефолтный параметр с '_' — только если нет сохранённого
            if (pf.IsDefault && savedUserParams.ContainsKey(pf.UrlKey))
                continue;

            root.Nodes.Add(new ColumnFilter
            {
                Column   = pf.UrlKey,
                Operator = pf.Operator,
                Value    = pf.Value
            });
        }
    }
}
