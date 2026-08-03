using System.Text.Json;
using Clayzor.Lib.Web.Controls.Components.Filter;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Сериализация/десериализация состояния динамического грида
/// для сохранения в ClayGridUserParams и восстановления.
/// Все методы — чистые функции (тестируются без БД).
/// </summary>
public static class GridStateSerializer
{
    private static readonly JsonSerializerOptions _filterJsonOptions = new()
    {
        Converters = { new ClayFilterJsonConverter() }
    };

    // ── Экранирование разделителей формата ──

    /// <summary>Экранирует разделители формата в значении токена: % , : → %25 %2C %3A.</summary>
    private static string Esc(string s)
        => s.Replace("%", "%25").Replace(",", "%2C").Replace(":", "%3A");

    /// <summary>Обратное преобразование к <see cref="Esc"/>. Порядок важен: %25 последним.</summary>
    private static string Unesc(string s)
        => s.Replace("%3A", ":").Replace("%2C", ",").Replace("%25", "%");

    // ── Колонки: "sql1:0,sql2:1,sql3:0" (SqlName:Order; 0=скрыта, 1=видима) ──

    /// <summary>Сериализует видимость и порядок колонок.</summary>
    public static string SerializeColumns(
        IReadOnlyList<int> columnOrder,
        IReadOnlyDictionary<int, ClayColumnMeta> columnById,
        IReadOnlySet<string> hidden)
    {
        var parts = columnOrder
            .Select(id => columnById.TryGetValue(id, out var m) ? m.SqlName : null)
            .Where(name => name is not null)
            .Select(name => $"{Esc(name!)}:{(hidden.Contains(name!) ? 0 : 1)}");
        return string.Join(",", parts);
    }

    /// <summary>Десериализует видимость и порядок колонок.</summary>
    public static List<(string SqlName, int Visible)> DeserializeColumns(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',')
            .Select(part =>
            {
                var idx = part.LastIndexOf(':');
                if (idx <= 0) return ((string?)null, 0);
                var name = Unesc(part[..idx]);
                return int.TryParse(part[(idx + 1)..], out var vis) ? (name, vis) : (null, 0);
            })
            .Where(t => t.Item1 is not null)
            .Select(t => (SqlName: t.Item1!, Visible: t.Item2))
            .ToList();
    }

    // ── Сортировка: "col1:asc,col2:desc" ──

    /// <summary>Сериализует состояние сортировки.</summary>
    public static string SerializeSort(IReadOnlyList<SortColumn> sortState)
    {
        var parts = sortState.Select(s => $"{Esc(s.Column)}:{(s.Desc ? "desc" : "asc")}");
        return string.Join(",", parts);
    }

    /// <summary>
    /// Десериализует состояние сортировки.
    /// Если задан <paramref name="allowedColumns"/> — колонки вне белого списка
    /// отбрасываются: в ORDER BY могут попасть только SortName зарегистрированных
    /// колонок (защита от инъекции через shared-ссылку, где значение параметра
    /// контролируется автором ссылки).
    /// </summary>
    /// <param name="value">Строка вида "col1:asc,col2:desc".</param>
    /// <param name="allowedColumns">Белый список допустимых имён колонок или null (без фильтрации).</param>
    public static List<SortColumn> DeserializeSort(string? value, ISet<string>? allowedColumns = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',')
            .Select(part =>
            {
                var idx = part.LastIndexOf(':');
                if (idx <= 0) return (null, false);
                return ((string?)Unesc(part[..idx]), part[(idx + 1)..] == "desc");
            })
            .Where(t => t.Item1 is not null)
            .Select(t => new SortColumn(t.Item1!, t.Item2))
            .Where(s => allowedColumns is null || allowedColumns.Contains(s.Column))
            .ToList();
    }

    // ── Группировка: "col1,col2" ──

    /// <summary>Сериализует список сгруппированных колонок.</summary>
    public static string SerializeGroups(IReadOnlyList<string> groupColumns)
        => string.Join(",", groupColumns.Select(Esc));

    /// <summary>Десериализует список сгруппированных колонок.</summary>
    public static List<string> DeserializeGroups(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Unesc).ToList();
    }

    // ── Фильтр: ClayFilterGroupNode ↔ JSON ──

    /// <summary>Сериализует дерево фильтра в JSON.</summary>
    public static string? SerializeFilter(ClayFilterGroupNode? root)
    {
        if (root is null || root.Nodes.Count == 0)
            return null;

        return JsonSerializer.Serialize<IClayFilterNode>(root, _filterJsonOptions);
    }

    /// <summary>Десериализует JSON в дерево фильтра.</summary>
    public static ClayFilterGroupNode? DeserializeFilter(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<IClayFilterNode>(json, _filterJsonOptions) as ClayFilterGroupNode;
    }

    // ── Размер страницы: int ↔ string ──

    /// <summary>Сериализует размер страницы.</summary>
    public static string SerializePageSize(int pageSize) => pageSize.ToString();

    /// <summary>Десериализует размер страницы.</summary>
    public static int? DeserializePageSize(string? value)
        => int.TryParse(value, out var n) && n > 0 ? n : null;
}
