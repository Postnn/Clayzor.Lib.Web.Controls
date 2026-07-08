namespace Clayzor.Lib.Web.Controls.Themes;

/// <summary>
/// Единый источник фирменных цветов Clayzor.
/// Значения синхронизированы с CSS-переменными --clay-* в app.css
/// (через эмитируемые MudBlazor переменные --mud-palette-*).
/// </summary>
public static class ClayColors
{
    // Core brand
    public const string Navy       = "#05164D";
    public const string NavyDark   = "#030F35";
    public const string NavyLight  = "#1A2D6B";
    public const string Navy2      = "#0A1D3D";
    public const string BlueMid    = "#00235F";

    // Accent
    public const string Gold       = "#FFAD00";
    public const string GoldDark   = "#E69C00";

    // Neutrals
    public const string White      = "#FFFFFF";
    public const string OffWhite   = "#F7F8FA";
    public const string GreyLight  = "#EBEDF0";
    public const string GreyMid    = "#9B9B9B";
    public const string GreyDark   = "#4A4A4A";
    public const string TextDark   = "#1A1A2E";

    // Semantic
    public const string ErrorRed   = "#C62828";
    public const string SuccessGreen = "#2E7D32";

    // MudBlazor palette extras
    public const string DrawerTextColor    = "#C8CDD8";
    public const string DrawerIconColor    = "#8A93A8";
    public const string ActionDisabledColor = "#B4B4B4";
    public const string TableStripedColor  = "#F2F4F7";
    public const string TableHoverColor    = "#E8EBF0";
    public const string LinesInputsColor   = "#C4C8D0";
}
