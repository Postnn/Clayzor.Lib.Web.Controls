using Clayzor.Lib.Web.Controls.Components.Filter;
using Dapper;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Параметры одной колонки сортировки: имя SQL-колонки и направление.
/// </summary>
/// <param name="Column">Имя SQL-колонки (русское название из БД).</param>
/// <param name="Desc">true — сортировка по убыванию, false — по возрастанию.</param>
public sealed record SortColumn(string Column, bool Desc);

/// <summary>
/// Тип данных колонки — определяет доступные операторы и поле ввода в диалоге фильтрации.
/// </summary>
public enum ColumnType
{
    /// <summary>Текстовая колонка — операторы Contains/Equals/StartsWith/EndsWith/NotEquals.</summary>
    Text,
    /// <summary>Целочисленная колонка — операторы Equals/NotEquals/GreaterThan/GreaterThanOrEqual/LessThan/LessThanOrEqual.</summary>
    Number,
    /// <summary>Дробная колонка (decimal/double/float) — те же операторы что и Number, редактор MudNumericField&lt;decimal?&gt;.</summary>
    Decimal,
    /// <summary>Колонка даты (DateTime/DateTimeOffset/DateOnly) — операторы сравнения + IsNull/IsNotNull.</summary>
    Date,
    /// <summary>Булевая колонка — оператор Equals, значение Да/Нет.</summary>
    Boolean,
}

/// <summary>
/// Оператор сравнения для фильтра по колонке.
/// </summary>
public enum ColumnFilterOperator
{
    /// <summary>Содержит подстроку (LIKE).</summary>
    Contains,
    /// <summary>Не содержит подстроку (NOT LIKE).</summary>
    NotContains,
    /// <summary>Равно.</summary>
    Equals,
    /// <summary>Не равно.</summary>
    NotEquals,
    /// <summary>Начинается с.</summary>
    StartsWith,
    /// <summary>Не начинается с (NOT LIKE).</summary>
    NotStartsWith,
    /// <summary>Заканчивается на.</summary>
    EndsWith,
    /// <summary>Не заканчивается на (NOT LIKE).</summary>
    NotEndsWith,
    /// <summary>Больше (&gt;).</summary>
    GreaterThan,
    /// <summary>Больше или равно (&gt;=).</summary>
    GreaterThanOrEqual,
    /// <summary>Меньше (&lt;).</summary>
    LessThan,
    /// <summary>Меньше или равно (&lt;=).</summary>
    LessThanOrEqual,
    /// <summary>Пустая строка / NULL — не требует значения.</summary>
    IsEmpty,
    /// <summary>Не пустая строка / NOT NULL — не требует значения.</summary>
    IsNotEmpty,
    /// <summary>NULL — не требует значения (для нестроковых типов).</summary>
    IsNull,
    /// <summary>NOT NULL — не требует значения (для нестроковых типов).</summary>
    IsNotNull,
}

/// <summary>
/// Логический оператор для объединения двух условий фильтрации.
/// </summary>
public enum LogicalOperator
{
    /// <summary>Логическое И.</summary>
    And,
    /// <summary>Логическое ИЛИ.</summary>
    Or,
}

/// <summary>
/// Режим отображения кнопки меню в заголовках колонок грида.
/// </summary>
public enum ColumnMenuMode
{
    /// <summary>Кнопка всегда скрыта.</summary>
    Hidden,
    /// <summary>Кнопка всегда видна (десктоп + мобильные).</summary>
    Always,
    /// <summary>Кнопка только на мобильных устройствах (≤960px).</summary>
    Mobile,
}

/// <summary>
/// <summary>
/// Списки доступных операторов фильтрации для каждого типа колонки.
/// </summary>
public static class ColumnFilterOperatorList
{
    /// <summary>Операторы для текстовых колонок.</summary>
    public static readonly IReadOnlyList<ColumnFilterOperator> TextOperators = [
        ColumnFilterOperator.Contains,
        ColumnFilterOperator.NotContains,
        ColumnFilterOperator.Equals,
        ColumnFilterOperator.NotEquals,
        ColumnFilterOperator.StartsWith,
        ColumnFilterOperator.NotStartsWith,
        ColumnFilterOperator.EndsWith,
        ColumnFilterOperator.NotEndsWith,
        ColumnFilterOperator.IsEmpty,
        ColumnFilterOperator.IsNotEmpty,
    ];

    /// <summary>Операторы для целочисленных колонок.</summary>
    public static readonly IReadOnlyList<ColumnFilterOperator> NumberOperators = [
        ColumnFilterOperator.Equals,
        ColumnFilterOperator.NotEquals,
        ColumnFilterOperator.GreaterThan,
        ColumnFilterOperator.GreaterThanOrEqual,
        ColumnFilterOperator.LessThan,
        ColumnFilterOperator.LessThanOrEqual,
        ColumnFilterOperator.IsNull,
        ColumnFilterOperator.IsNotNull,
    ];

    /// <summary>Операторы для дробных колонок (те же что и Number).</summary>
    public static readonly IReadOnlyList<ColumnFilterOperator> DecimalOperators = [
        ColumnFilterOperator.Equals,
        ColumnFilterOperator.NotEquals,
        ColumnFilterOperator.GreaterThan,
        ColumnFilterOperator.GreaterThanOrEqual,
        ColumnFilterOperator.LessThan,
        ColumnFilterOperator.LessThanOrEqual,
        ColumnFilterOperator.IsNull,
        ColumnFilterOperator.IsNotNull,
    ];

    /// <summary>Операторы для колонок дат.</summary>
    public static readonly IReadOnlyList<ColumnFilterOperator> DateOperators = [
        ColumnFilterOperator.Equals,
        ColumnFilterOperator.NotEquals,
        ColumnFilterOperator.GreaterThan,
        ColumnFilterOperator.GreaterThanOrEqual,
        ColumnFilterOperator.LessThan,
        ColumnFilterOperator.LessThanOrEqual,
        ColumnFilterOperator.IsNull,
        ColumnFilterOperator.IsNotNull,
    ];

    /// <summary>Операторы для булевых колонок.</summary>
    public static readonly IReadOnlyList<ColumnFilterOperator> BooleanOperators = [
        ColumnFilterOperator.Equals,
        ColumnFilterOperator.IsNull,
        ColumnFilterOperator.IsNotNull,
    ];
}

/// <summary>
/// Текущее состояние запроса к данным (поиск, группировка, сортировка, фильтрация по колонкам).
/// Предоставляет методы <see cref="BuildOrderBy"/>, <see cref="BuildWhereClause"/>
/// и <see cref="BuildColumnFilterClause"/> для генерации фрагментов SQL.
/// </summary>
public sealed class ClayDataQuery
{
    /// <summary>Текст поискового запроса. null или пустая строка — поиск не активен.</summary>
    public string? SearchText { get; set; }

    /// <summary>Включена ли группировка данных.</summary>
    public bool GroupEnabled { get; set; }

    /// <summary>SQL-имена колонок, по которым выполняется группировка (в порядке приоритета).</summary>
    public List<string> GroupColumns { get; set; } = [];

    /// <summary>Набор развёрнутых групп (полные ключи через \u001F). Пустой = все свёрнуты.</summary>
    public HashSet<string> ExpandedGroups { get; set; } = [];

    /// <summary>Список колонок сортировки в порядке приоритета.</summary>
    public List<SortColumn> SortColumns { get; set; } = [];

    /// <summary>Номер текущей страницы (1-based).</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>Размер страницы (количество записей).</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Общее количество записей, соответствующих запросу. Заполняется страницей после загрузки данных.</summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Дерево условий составного фильтра.
    /// Единый источник истины фильтрации; заменяет прежний словарь <c>ColumnFilters</c>.
    /// null или пустой корень — без фильтрации.
    /// </summary>
    public Filter.ClayFilterGroupNode? CompositeFilter { get; set; }

    /// <summary>
    /// Условия фильтрации по отдельным колонкам. Ключ — SQL-имя колонки, значение — условие фильтра.
    /// Управляется страницей; ClayGrid не изменяет этот словарь.
    /// </summary>
    [Obsolete("Используйте CompositeFilter. ColumnFilters упраздняется в задаче 10.")]
    public Dictionary<string, ColumnFilter> ColumnFilters { get; set; } = [];

    /// <summary>
    /// Строит фрагмент WHERE для фильтрации по колонкам из <see cref="ColumnFilters"/>.
    /// Поддерживает до двух условий на колонку, объединяемых через <see cref="ColumnFilter.LogicalOperator"/>.
    /// Возвращает null, если нет активных фильтров.
    /// Параметры добавляются в <paramref name="parameters"/> через Dapper <c>DynamicParameters</c>.
    /// </summary>
    /// <param name="parameters">Объект DynamicParameters, в который добавляются параметры фильтра.</param>
    /// <param name="columnNameMap">
    /// Необязательный маппинг SQL-имён колонок: ключ — имя из <see cref="ColumnFilter.Column"/>,
    /// значение — имя для подстановки в SQL-выражение.
    /// Используется в плоском режиме, где имена колонок отличаются от подзапросного режима
    /// (например, <c>"TestTypeName"</c> → <c>"t.ТипМедицинскогоАнализа"</c>).
    /// </param>
    /// <returns>Строка для вставки в WHERE (без ключевого слова WHERE), либо null.</returns>
    [Obsolete("Используйте ClayCompositeSqlBuilder.Build с CompositeFilter.")]
#pragma warning disable CS0618
    public string? BuildColumnFilterClause(DynamicParameters parameters,
        Dictionary<string, string>? columnNameMap = null)
    {
        var parts = new List<string>();
        foreach (var cf in ColumnFilters.Values)
        {
            if (!cf.HasValue) continue;
            // Применяем маппинг имён если задан
            var colName = columnNameMap is not null && columnNameMap.TryGetValue(cf.Column, out var mapped)
                ? mapped
                : cf.Column;

            var clause1 = BuildSingleClause(colName, cf.ParamName, cf.Operator, cf.Value, parameters);
            if (clause1 is null) continue;

            if (cf.HasSecondClause)
            {
                var clause2 = BuildSingleClause(colName, cf.SecondParamName, cf.SecondOperator, cf.SecondValue, parameters);
                if (clause2 is not null)
                {
                    var logic = cf.LogicalOperator == LogicalOperator.Or ? "OR" : "AND";
                    parts.Add($"({clause1} {logic} {clause2})");
                }
                else
                {
                    parts.Add(clause1);
                }
            }
            else
            {
                parts.Add(clause1);
            }
        }
        return parts.Count > 0 ? string.Join(" AND ", parts) : null;
    }
#pragma warning restore CS0618

    /// <summary>
    /// Строит SQL-выражение для одного условия фильтрации.
    /// Возвращает null, если значение отсутствует (для операторов, требующих значение).
    /// </summary>
    internal static string? BuildSingleClause(string colName, string paramName, ColumnFilterOperator op, object? value, DynamicParameters dp)
    {
        switch (op)
        {
            case ColumnFilterOperator.IsEmpty:
                return $"({colName} IS NULL OR {colName} = '')";
            case ColumnFilterOperator.IsNotEmpty:
                return $"({colName} IS NOT NULL AND {colName} <> '')";
            case ColumnFilterOperator.IsNull:
                return $"{colName} IS NULL";
            case ColumnFilterOperator.IsNotNull:
                return $"{colName} IS NOT NULL";
            case ColumnFilterOperator.Contains:
                dp.Add(paramName, $"%{EscapeLikeValue(value)}%");
                return $"{colName} LIKE @{paramName} ESCAPE '\\'";
            case ColumnFilterOperator.NotContains:
                dp.Add(paramName, $"%{EscapeLikeValue(value)}%");
                return $"{colName} NOT LIKE @{paramName} ESCAPE '\\'";
            case ColumnFilterOperator.StartsWith:
                dp.Add(paramName, $"{EscapeLikeValue(value)}%");
                return $"{colName} LIKE @{paramName} ESCAPE '\\'";
            case ColumnFilterOperator.NotStartsWith:
                dp.Add(paramName, $"{EscapeLikeValue(value)}%");
                return $"{colName} NOT LIKE @{paramName} ESCAPE '\\'";
            case ColumnFilterOperator.EndsWith:
                dp.Add(paramName, $"%{EscapeLikeValue(value)}");
                return $"{colName} LIKE @{paramName} ESCAPE '\\'";
            case ColumnFilterOperator.NotEndsWith:
                dp.Add(paramName, $"%{EscapeLikeValue(value)}");
                return $"{colName} NOT LIKE @{paramName} ESCAPE '\\'";
            case ColumnFilterOperator.Equals:
                dp.Add(paramName, value);
                return $"{colName} = @{paramName}";
            case ColumnFilterOperator.NotEquals:
                dp.Add(paramName, value);
                return $"{colName} <> @{paramName}";
            case ColumnFilterOperator.GreaterThan:
                dp.Add(paramName, value);
                return $"{colName} > @{paramName}";
            case ColumnFilterOperator.GreaterThanOrEqual:
                dp.Add(paramName, value);
                return $"{colName} >= @{paramName}";
            case ColumnFilterOperator.LessThan:
                dp.Add(paramName, value);
                return $"{colName} < @{paramName}";
            case ColumnFilterOperator.LessThanOrEqual:
                dp.Add(paramName, value);
                return $"{colName} <= @{paramName}";
            default:
                dp.Add(paramName, value);
                return $"{colName} = @{paramName}";
        }
    }

    /// <summary>
    /// Экранирует спецсимволы LIKE в пользовательском значении для использования с <c>ESCAPE '\'</c>.
    /// <c>\</c> → <c>\\</c>, <c>%</c> → <c>\%</c>, <c>_</c> → <c>\_</c>, <c>[</c> → <c>[[]</c>.
    /// Одинарная кавычка (<c>'</c>) не экранируется — значения передаются через Dapper-параметры.
    /// </summary>
    private static string EscapeLikeValue(object? value)
    {
        var s = value?.ToString() ?? "";
        return s.Replace("\\", "\\\\")
                .Replace("[", "[[]")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
    }

    /// <summary>
    /// Строит фрагмент ORDER BY с учётом группировки и сортировки.
    /// Если группировка включена, её колонка идёт первой.
    /// Если сортировка не задана, используется <paramref name="defaultOrder"/>.
    /// </summary>
    /// <param name="defaultOrder">Порядок сортировки по умолчанию, например "Порядок, НазваниеАнализа".</param>
    /// <param name="allowedColumns">
    /// Белый список допустимых выражений ORDER BY (SortName зарегистрированных колонок).
    /// Если задан — колонки вне списка отбрасываются. <c>null</c> — без фильтрации.
    /// </param>
    /// <returns>Строка для вставки в ORDER BY. Если после фильтрации список пуст, возвращается <paramref name="defaultOrder"/>.</returns>
    public string BuildOrderBy(string defaultOrder, ISet<string>? allowedColumns = null)
    {
        bool Allowed(string col) => allowedColumns is null || allowedColumns.Contains(col);

        var clauses = new List<string>();

        if (GroupEnabled && GroupColumns.Count > 0)
        {
            foreach (var gc in GroupColumns)
            {
                if (!Allowed(gc)) continue;
                var sortCol = SortColumns.Find(s => s.Column == gc);
                if (sortCol is not null)
                    clauses.Add($"{gc} {(sortCol.Desc ? "DESC" : "ASC")}");
                else
                    clauses.Add(gc);
            }
        }

        if (SortColumns.Count == 0)
        {
            foreach (var col in defaultOrder.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                if (GroupEnabled && GroupColumns.Contains(col))
                    continue;
                // defaultOrder — из определения грида, доверенная строка; проверка Allowed не требуется.
                clauses.Add(col);
            }
        }
        else
        {
            foreach (var s in SortColumns)
            {
                if (!Allowed(s.Column)) continue;
                if (GroupEnabled && GroupColumns.Contains(s.Column))
                    continue;
                clauses.Add($"{s.Column} {(s.Desc ? "DESC" : "ASC")}");
            }
        }

        return clauses.Count > 0 ? string.Join(", ", clauses) : defaultOrder;
    }

    /// <summary>
    /// Строит фрагмент WHERE с LIKE-поиском по указанным колонкам.
    /// Возвращает null, если текст поиска пуст.
    /// Использует параметр @search, значение подставляется через Dapper.
    /// </summary>
    /// <param name="searchColumns">Имена SQL-колонок для поиска, например "a.НазваниеАнализа", "t.ТипМедицинскогоАнализа".</param>
    /// <returns>Строка для вставки в WHERE, либо null.</returns>
    public string? BuildWhereClause(params string[] searchColumns)
    {
        if (string.IsNullOrWhiteSpace(SearchText) || searchColumns.Length == 0)
            return null;

        return string.Join(" OR ", searchColumns.Select(c => $"{c} LIKE @search"));
    }

    /// <summary>
    /// Объединяет два WHERE-фрагмента через AND.
    /// Если оба null — возвращает null.
    /// Если один null — возвращает другой без обёртки в скобки.
    /// Если оба не null — возвращает <c>({a}) AND ({b})</c>.
    /// </summary>
    /// <param name="a">Первый WHERE-фрагмент (например, из <see cref="BuildWhereClause"/>).</param>
    /// <param name="b">Второй WHERE-фрагмент (например, из <see cref="BuildColumnFilterClause"/>).</param>
    public static string? CombineWhere(string? a, string? b)
    {
        if (a is null && b is null) return null;
        if (a is null) return b;
        if (b is null) return a;
        return $"({a}) AND ({b})";
    }
}
