namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Маппинг строк агрегатного GROUP BY-запроса (словарь колонка→значение из
/// <see cref="Clayzor.Lib.Entities.DynamicGrid.DynamicSql.QueryRowsAsync"/>)
/// в <see cref="GridGroupRow"/> для <see cref="ClayGroupingEngine"/>.
/// Имена колонок K0/K1/K2/Cnt задаёт <see cref="ClayGroupingEngine.BuildGroupAggregateSql"/>.
/// ЧИСТАЯ функция — тестируется без БД.
/// </summary>
public static class ClayDynamicGroupMapper
{
    /// <summary>Имена колонок агрегатного запроса — должны совпадать с BuildGroupAggregateSql.</summary>
    private const string K0 = "K0";
    private const string K1 = "K1";
    private const string K2 = "K2";
    private const string Cnt = "Cnt";

    /// <summary>Мапит одну строку агрегата. DBNull приводится к null.</summary>
    public static GridGroupRow MapRow(IReadOnlyDictionary<string, object?> row) => new()
    {
        K0  = Normalize(row, K0) ?? "",
        K1  = Normalize(row, K1),
        K2  = Normalize(row, K2),
        Cnt = ToInt(Normalize(row, Cnt)),
    };

    /// <summary>Мапит все строки агрегата, сохраняя порядок из БД (он значим для BuildTree).</summary>
    public static List<GridGroupRow> MapRows(IEnumerable<IReadOnlyDictionary<string, object?>> rows)
        => rows.Select(MapRow).ToList();

    private static object? Normalize(IReadOnlyDictionary<string, object?> row, string key)
    {
        var v = row.GetValueOrDefault(key);
        return v is DBNull ? null : v;
    }

    private static int ToInt(object? v) => v switch
    {
        null   => 0,
        int i  => i,
        _      => Convert.ToInt32(v),
    };
}
