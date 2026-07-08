namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Временное хранилище имени перетаскиваемой SQL-колонки
/// между dragstart (на заголовке) и drop (на grouping tray).
/// Используется вместо DataTransfer.GetData, недоступного в Blazor .NET.
/// Безопасно для Blazor Server — события обрабатываются последовательно в одном circuit.
/// </summary>
public static class ClayDragState
{
    /// <summary>SQL-имя колонки, которую перетаскивают в данный момент. null — нет активного перетаскивания.</summary>
    public static string? DraggedColumn { get; set; }
}
