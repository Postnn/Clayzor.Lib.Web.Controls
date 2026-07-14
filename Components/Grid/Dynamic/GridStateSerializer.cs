using System.Text.Json;
using Clayzor.Lib.Web.Controls.Components.Grid.Filter;

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
            .Select(name => $"{name}:{(hidden.Contains(name!) ? 0 : 1)}");
        return string.Join(",", parts);
    }

    /// <summary>Десериализует видимость и порядок колонок.</summary>
    public static List<(string SqlName, int Visible)> DeserializeColumns(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',')
            .Select(part => part.Split(':'))
            .Where(p => p.Length == 2 && int.TryParse(p[1], out _))
            .Select(p => (SqlName: p[0], Visible: int.Parse(p[1])))
            .ToList();
    }

    // ── Сортировка: "col1:asc,col2:desc" ──

    /// <summary>Сериализует состояние сортировки.</summary>
    public static string SerializeSort(IReadOnlyList<SortColumn> sortState)
    {
        var parts = sortState.Select(s => $"{s.Column}:{(s.Desc ? "desc" : "asc")}");
        return string.Join(",", parts);
    }

    /// <summary>Десериализует состояние сортировки.</summary>
    public static List<SortColumn> DeserializeSort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',')
            .Select(part => part.Split(':'))
            .Where(p => p.Length == 2)
            .Select(p => new SortColumn(p[0], p[1] == "desc"))
            .ToList();
    }

    // ── Группировка: "col1,col2" ──

    /// <summary>Сериализует список сгруппированных колонок.</summary>
    public static string SerializeGroups(IReadOnlyList<string> groupColumns)
        => string.Join(",", groupColumns);

    /// <summary>Десериализует список сгруппированных колонок.</summary>
    public static List<string> DeserializeGroups(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
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
