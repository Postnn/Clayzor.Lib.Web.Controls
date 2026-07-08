namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>
/// Символы для печатных форм грида Clayzor.
/// Визуально соответствуют MudBlazor-иконкам, используемым в гриде.
/// </summary>
public static class ClayGridPrintStyles
{
    // ── Иконки групп (аналог MudBlazor ChevronRight / ExpandMore) ──
    public const string GroupCollapsedIcon = "▸";
    public const string GroupExpandedIcon  = "▾";

    // ── Булевы значения (аналог MudBlazor CheckCircle / Cancel) ──
    public const string BoolTrueIcon  = "✓";
    public const string BoolFalseIcon = "✗";

    // ── Состояние выбора группы (indeterminate) ──
    public const string GroupIndeterminateIcon = "⊟";
}
