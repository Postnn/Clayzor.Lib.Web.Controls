namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Результат запроса уникальных значений колонки для фильтра по значению (Excel-style).
/// Возвращается методом <see cref="IClayGridDataLoader.LoadDistinctValuesAsync"/>.
/// </summary>
public sealed class DistinctValuesResult
{
    /// <summary>
    /// Уникальные значения колонки (без пустышек), не больше лимита.
    /// Пустой список, если <see cref="Capped"/> = <c>true</c>.
    /// Значения сохраняют исходный CLR-тип (int, string, DateTime, bool, …) —
    /// не приводятся к строке.
    /// </summary>
    public IReadOnlyList<object?> Values { get; init; } = [];

    /// <summary>
    /// <c>true</c> — уникальных (не-пустых) значений больше лимита (по умолчанию 100).
    /// Список <see cref="Values"/> при этом пуст.
    /// </summary>
    public bool Capped { get; init; }

    /// <summary>
    /// <c>true</c> — в колонке есть строки с <c>NULL</c> или пустой строкой.
    /// UI использует этот флаг для отображения пункта «(пустые)».
    /// </summary>
    public bool HasBlanks { get; init; }

    /// <summary>
    /// Полное количество уникальных не-пустых значений (без учёта лимита).
    /// Имеет смысл только когда <see cref="Capped"/> = <c>false</c>; используется
    /// для вычисления инверсии (требование 14).
    /// </summary>
    public int TotalDistinct { get; init; }
}
