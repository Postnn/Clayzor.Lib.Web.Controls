using System.Text;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Clayzor.Lib.Web.Controls.Themes;

/// <summary>
/// Корпоративная тема в стиле Lufthansa: глубокий тёмно-синий, золотой акцент, чистый белый.
/// </summary>
public static class ClayTheme
{
    /// <summary>
    /// Создаёт и возвращает настроенную тему MudBlazor в корпоративном стиле.
    /// Цвета берутся из <see cref="ClayColors"/> — единого источника brand-значений.
    /// </summary>
    /// <returns>Готовая тема <see cref="MudTheme"/>.</returns>
    public static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            // Core
            Primary = ClayColors.Navy,
            PrimaryDarken = ClayColors.NavyDark,
            PrimaryLighten = ClayColors.NavyLight,
            Secondary = ClayColors.Gold,
            SecondaryDarken = ClayColors.GoldDark,
            Tertiary = ClayColors.BlueMid,

            // App bar
            AppbarBackground = ClayColors.Navy,
            AppbarText = ClayColors.White,

            // Background & Surface
            Background = ClayColors.OffWhite,
            Surface = ClayColors.White,
            BackgroundGray = ClayColors.OffWhite,

            // Drawer / Sidebar
            DrawerBackground = ClayColors.Navy2,
            DrawerText = ClayColors.DrawerTextColor,
            DrawerIcon = ClayColors.DrawerIconColor,

            // Text
            TextPrimary = ClayColors.TextDark,
            TextSecondary = ClayColors.NavyMid,
            TextDisabled = ClayColors.GreyMid,

            // Actions
            ActionDefault = ClayColors.Navy,
            ActionDisabled = ClayColors.ActionDisabledColor,
            ActionDisabledBackground = ClayColors.GreyLight,

            // Semantic
            Error = ClayColors.ErrorRed,
            Warning = ClayColors.Gold,
            Info = ClayColors.BlueMid,
            Success = ClayColors.SuccessGreen,

            // Table
            TableLines = ClayColors.GreyLight,
            TableStriped = ClayColors.TableStripedColor,
            TableHover = ClayColors.TableHoverColor,

            // Misc
            Divider = ClayColors.GreyLight,
            DividerLight = ClayColors.GreyLight,
            LinesDefault = ClayColors.GreyLight,
            LinesInputs = ClayColors.LinesInputsColor,
        },

        PaletteDark = new PaletteDark
        {
            Primary = "#4A7CFF",
            Secondary = "#FFAD00",
            AppbarBackground = "#0B1529",
            Surface = "#1A1F33",
            Background = "#0E1220",
            BackgroundGray = "#161B2E",
            DrawerBackground = "#0B1529",
            DrawerText = "#A0A8BF",
            TextPrimary = "#E4E7EF",
            TextSecondary = "#8A93A8",
            TableLines = "#2A3050",
            TableStriped = "#161B2E",
            TableHover = "#1F2540",
            Divider = "#2A3050",
            Error = "#EF5350",
            Warning = "#FFB74D",
            Info = "#42A5F5",
            Success = "#66BB6A",
        },

        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["var(--clay-font-family)"],
                FontSize = "var(--clay-font-size)",
                FontWeight = "400",
                LineHeight = "1.5",
                LetterSpacing = "0.01em",
            },
            H4 = new H4Typography
            {
                FontWeight = "300",
                FontSize = "2.25rem",
                LineHeight = "1.2",
                LetterSpacing = "0",
            },
            H5 = new H5Typography
            {
                FontWeight = "700",
                FontSize = "1.5rem",
                LineHeight = "1.3",
            },
            H6 = new H6Typography
            {
                FontWeight = "600",
                FontSize = "1.05rem",
                LineHeight = "1.4",
            },
            Body1 = new Body1Typography
            {
                FontSize = "var(--clay-font-size)",
                LineHeight = "1.6",
            },
            Body2 = new Body2Typography
            {
                FontSize = "var(--clay-font-size)",
                LineHeight = "1.5",
            },
            Subtitle1 = new Subtitle1Typography
            {
                FontSize = "var(--clay-font-size)",
            },
            Subtitle2 = new Subtitle2Typography
            {
                FontSize = "var(--clay-font-size)",
            },
            Caption = new CaptionTypography
            {
                FontSize = "var(--clay-font-size-secondary)",
            },
            Overline = new OverlineTypography
            {
                FontSize = "var(--clay-font-size-secondary)",
            },
            Button = new ButtonTypography
            {
                FontWeight = "700",
                FontSize = "var(--clay-font-size)",
                LetterSpacing = "normal",
                TextTransform = "none",
            },
        },

        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "var(--clay-radius)",
            DrawerWidthLeft = "260px",
        }
    };

    /// <summary>Создаёт независимый экземпляр выбранной темы с общей типографикой и геометрией Clayzor.</summary>
    /// <param name="preference">Сочетание темы и режима.</param>
    /// <returns>Настроенная тема MudBlazor.</returns>
    public static MudTheme Create(ClayThemePreference preference)
    {
        var theme = Create();
        if (GetColors(preference) is not { } colors)
            return theme;

        theme.PaletteLight = new PaletteLight
        {
            Primary = colors.Focus,
            Secondary = colors.Accent,
            SecondaryContrastText = ClayColors.White,
            Tertiary = colors.Text,
            AppbarBackground = colors.Toolbar,
            AppbarText = colors.Text,
            Background = colors.Canvas,
            Surface = colors.Surface,
            BackgroundGray = colors.Toolbar,
            DrawerBackground = colors.Toolbar,
            DrawerText = colors.Text,
            DrawerIcon = colors.Text,
            TextPrimary = colors.Text,
            TextSecondary = colors.Muted,
            TextDisabled = colors.Muted,
            ActionDefault = colors.Text,
            ActionDisabled = colors.Muted,
            ActionDisabledBackground = colors.Disabled,
            Error = "#B32624",
            Warning = "#975000",
            Info = "#29659A",
            Success = "#246A45",
            TableLines = colors.Border,
            TableStriped = colors.Stripe,
            TableHover = colors.Hover,
            Divider = colors.Border,
            DividerLight = colors.Border,
            LinesDefault = colors.Border,
            LinesInputs = colors.InputBorder,
        };
        return theme;
    }

    internal static ThemeColors? GetColors(ClayThemePreference preference) => preference switch
    {
        ClayThemePreference.LegacyLight => Legacy,
        ClayThemePreference.CorporateLight => Corporate,
        ClayThemePreference.ModernLight => Modern,
        _ => null,
    };

    private static readonly ThemeColors Legacy = new(
        Text: "#222222", Muted: "#555555", Canvas: "#EFEFEF", Surface: "#EFEFEF",
        GridHeader: "#4078A8", GridHeaderText: ClayColors.White, Stripe: "#EFEFEF", Hover: "#DDEBF8",
        Selected: "#C8DEF3", Border: "#AAAAAA", InputBorder: "#777777", Button: "#F6F6F6",
        ButtonText: "#222222", ButtonHover: "#DEDEDE", Dialog: "#CCCCCC", DialogText: "#222222",
        Accent: "#2574BA", Add: "#2574BA", Focus: "#2574BA", Toolbar: "#DCDCDC",
        Disabled: "#DEDEDE", MenuHover: "#C5C5C5");

    private static readonly ThemeColors Corporate = new(
        Text: "#252D34", Muted: "#58636D", Canvas: "#F8F9FA", Surface: ClayColors.White,
        GridHeader: "#3D454D", GridHeaderText: ClayColors.White, Stripe: "#F1F2F3", Hover: "#E8ECF0",
        Selected: "#DCE4E9", Border: "#BFC6CD", InputBorder: "#78838C", Button: "#3C474F",
        ButtonText: ClayColors.White, ButtonHover: "#303A41", Dialog: "#353E46", DialogText: ClayColors.White,
        Accent: "#58656E", Add: "#58656E", Focus: "#496A84", Toolbar: "#F0F1F2",
        Disabled: "#ECEEF0", MenuHover: "#DCE4E9");

    private static readonly ThemeColors Modern = new(
        Text: "#153F37", Muted: "#4F6E66", Canvas: "#FBFDFB", Surface: ClayColors.White,
        GridHeader: "#DDF2E9", GridHeaderText: "#105A4B", Stripe: "#F0FAF6", Hover: "#E2F5ED",
        Selected: "#D6F2E7", Border: "#BCD4CC", InputBorder: "#658B7F", Button: "#007F69",
        ButtonText: ClayColors.White, ButtonHover: "#006B58", Dialog: "#DDF2E9", DialogText: "#145A4C",
        Accent: "#007F69", Add: "#007F69", Focus: "#007F69", Toolbar: "#F4FAF7",
        Disabled: "#F0F3F1", MenuHover: "#D6F2E7");

    internal sealed record ThemeColors(
        string Text, string Muted, string Canvas, string Surface, string GridHeader, string GridHeaderText,
        string Stripe, string Hover, string Selected, string Border, string InputBorder, string Button,
        string ButtonText, string ButtonHover, string Dialog, string DialogText, string Accent, string Add,
        string Focus, string Toolbar, string Disabled, string MenuHover);
}

/// <summary>Публикует доверенные цветовые токены Clayzor в том же CSS scope, что и палитру MudBlazor.</summary>
public sealed class ClayThemeTokenProvider : MudThemeProvider
{
    /// <summary>Тема, цветовые токены которой публикуются в CSS.</summary>
    [Parameter] public ClayThemePreference Preference { get; set; }

    /// <summary>Добавляет ограниченный набор цветовых переменных к палитре MudBlazor.</summary>
    /// <param name="css">Буфер генерируемых правил темы.</param>
    protected override void GenerateTheme(StringBuilder css)
    {
        base.GenerateTheme(css);
        css.AppendLine($"--clay-input-required-bg: {ClayColors.EmptyRequiredBackground};");
        css.AppendLine($"--clay-input-required-fg: {ClayColors.TextDark};");
        css.AppendLine($"--clay-swatch-classic-light: {ClayColors.Gold};");
        css.AppendLine($"--clay-swatch-classic-dark: {ClayColors.Navy};");
        css.AppendLine($"--clay-swatch-legacy: {ClayTheme.GetColors(ClayThemePreference.LegacyLight)!.GridHeader};");
        css.AppendLine($"--clay-swatch-corporate: {ClayTheme.GetColors(ClayThemePreference.CorporateLight)!.GridHeader};");
        css.AppendLine($"--clay-swatch-modern: {ClayTheme.GetColors(ClayThemePreference.ModernLight)!.Accent};");
        var colors = ClayTheme.GetColors(Preference);
        var focusColor = colors?.Focus ?? (Preference == ClayThemePreference.ClassicDark ? "#92BEFF" : ClayColors.BlueMid);
        css.AppendLine($"--clay-focus: {focusColor};");
        const string dirtyOnLight = "#793C00";
        var dirtyColor = Preference == ClayThemePreference.ClassicDark ? "#FFC479" : dirtyOnLight;
        css.AppendLine($"--clay-dirty: {dirtyColor};");
        css.AppendLine($"--clay-dirty-on-required: {dirtyOnLight};");
        if (colors is null)
            return;

        css.AppendLine($"--clay-grid-header-bg: {colors.GridHeader};");
        css.AppendLine($"--clay-grid-header-fg: {colors.GridHeaderText};");
        css.AppendLine($"--clay-grid-row-bg: {ClayColors.White};");
        css.AppendLine($"--clay-grid-row-hover: {colors.Hover};");
        css.AppendLine($"--clay-grid-row-selected: {colors.Selected};");
        css.AppendLine($"--clay-dialog-header-bg: {colors.Dialog};");
        css.AppendLine($"--clay-dialog-header-fg: {colors.DialogText};");
        css.AppendLine($"--clay-command-bg: {colors.Toolbar};");
        css.AppendLine($"--clay-primary-button-bg: {colors.Button};");
        css.AppendLine($"--clay-primary-button-fg: {colors.ButtonText};");
        css.AppendLine($"--clay-primary-button-hover: {colors.ButtonHover};");
        css.AppendLine($"--clay-secondary-button-bg: {ClayColors.White};");
        css.AppendLine($"--clay-secondary-button-fg: {colors.Text};");
        css.AppendLine($"--clay-accent-color: {colors.Accent};");
        css.AppendLine($"--clay-add-action: {colors.Add};");
        css.AppendLine($"--clay-input-bg: {ClayColors.White};");
        css.AppendLine($"--clay-menu-bg: {colors.Toolbar};");
        css.AppendLine($"--clay-menu-hover: {colors.MenuHover};");
    }
}
