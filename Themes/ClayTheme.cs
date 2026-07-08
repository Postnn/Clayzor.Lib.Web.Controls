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
            TextSecondary = ClayColors.GreyDark,
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
                FontWeight = "700",
                FontSize = "1.75rem",
                LineHeight = "1.2",
                LetterSpacing = "-0.01em",
            },
            H5 = new H5Typography
            {
                FontWeight = "600",
                FontSize = "1.25rem",
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
                FontSize = "var(--clay-font-size)",
            },
            Overline = new OverlineTypography
            {
                FontSize = "var(--clay-font-size)",
            },
            Button = new ButtonTypography
            {
                FontWeight = "600",
                FontSize = "0.8125rem",
                LetterSpacing = "0.04em",
                TextTransform = "uppercase",
            },
        },

        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "2px",
            DrawerWidthLeft = "260px",
        }
    };
}
