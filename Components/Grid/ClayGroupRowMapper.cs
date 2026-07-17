namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Маппинг строк агрегатного GROUP BY-запроса (словарь колонка→значение) в
/// <see cref="GridGroupRow"/>. Имена колонок K{i}/Cnt задаёт
/// <see cref="ClayGroupingEngine.BuildGroupAggregateSql"/>.
/// Общий для статического и динамического режимов. ЧИСТЫЙ класс — тестируется без БД.
/// </summary>
public static class ClayGroupRowMapper
{
    private const string CountColumn = "Cnt";

    /// <summary>
    /// Мапит одну строку агрегата.
    /// </summary>
    /// <param name="row">Строка результата (Dapper-словарь или словарь DynamicSql).</param>
    /// <param name="levelCount">
    /// Число уровней группировки. Задаётся вызывающим по числу группировочных колонок,
    /// а НЕ угадывается по наличию ключей в строке: null — законное значение ключа.
    /// </param>
    public static GridGroupRow MapRow(IReadOnlyDictionary<string, object?> row, int levelCount)
    {
        var keys = new List<object?>(levelCount);
        for (int i = 0; i < levelCount; i++)
            keys.Add(Normalize(row.GetValueOrDefault($"K{i}")));

        return new GridGroupRow
        {
            Keys = keys,
            Cnt  = ToInt(Normalize(row.GetValueOrDefault(CountColumn))),
        };
    }

    /// <summary>
    /// Мапит все строки агрегата, СОХРАНЯЯ порядок из БД: BuildTree требует, чтобы
    /// родительский агрегат предшествовал дочернему, и этот порядок задаёт ORDER BY агрегата.
    /// </summary>
    public static List<GridGroupRow> MapRows(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows, int levelCount)
        => rows.Select(r => MapRow(r, levelCount)).ToList();

    /// <summary>DBNull → null. BuildAggregates различает null и значение, DBNull его обманет.</summary>
    private static object? Normalize(object? v) => v is DBNull ? null : v;

    private static int ToInt(object? v) => v switch
    {
        null  => 0,
        int i => i,
        _     => Convert.ToInt32(v),
    };
}
