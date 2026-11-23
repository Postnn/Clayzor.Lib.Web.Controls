namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Состояние перетаскивания колонки, принадлежащее одному экземпляру грида.
/// Используется вместо DataTransfer.GetData, недоступного в Blazor .NET.
/// </summary>
public sealed class ClayDragState
{
    /// <summary>SQL-имя колонки, которую перетаскивают в данный момент. null — нет активного перетаскивания.</summary>
    public string? DraggedColumn { get; set; }
}
