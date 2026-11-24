namespace Clayzor.Lib.Web.Controls.Themes;

/// <summary>Поддерживаемое сочетание цветовой темы и светлого или тёмного режима.</summary>
public enum ClayThemePreference
{
    /// <summary>Классическая светлая тема Clayzor.</summary>
    ClassicLight,

    /// <summary>Классическая тёмная тема Clayzor.</summary>
    ClassicDark,

    /// <summary>Светлая тема в стиле Kesco.</summary>
    LegacyLight,

    /// <summary>Светлая графитовая тема.</summary>
    CorporateLight,

    /// <summary>Светлая тема с бирюзовыми акцентами.</summary>
    ModernLight,
}

/// <summary>Каталог, сериализация и отображаемые названия тем Clayzor.</summary>
public static class ClayThemeCatalog
{
    /// <summary>Все доступные пользователю сочетания темы и режима.</summary>
    public static IReadOnlyList<ClayThemePreference> Available { get; } =
        Array.AsReadOnly(Enum.GetValues<ClayThemePreference>());

    /// <summary>Разбирает сохранённое значение с безопасным возвратом к поддерживаемой теме.</summary>
    /// <param name="value">Сохранённое сочетание темы и режима.</param>
    /// <returns>Поддерживаемое сочетание; для неизвестного значения — Classic Light.</returns>
    public static ClayThemePreference Parse(string? value) => value switch
    {
        "classic:dark" => ClayThemePreference.ClassicDark,
        "legacy:light" or "legacy:dark" => ClayThemePreference.LegacyLight,
        "corporate:light" or "corporate:dark" => ClayThemePreference.CorporateLight,
        "modern:light" or "modern:dark" => ClayThemePreference.ModernLight,
        _ => ClayThemePreference.ClassicLight,
    };

    /// <summary>Возвращает стабильное значение для хранения темы в настройках.</summary>
    /// <param name="preference">Сочетание темы и режима.</param>
    public static string Serialize(this ClayThemePreference preference) => preference switch
    {
        ClayThemePreference.ClassicLight => "classic:light",
        ClayThemePreference.ClassicDark => "classic:dark",
        ClayThemePreference.LegacyLight => "legacy:light",
        ClayThemePreference.CorporateLight => "corporate:light",
        ClayThemePreference.ModernLight => "modern:light",
        _ => throw new ArgumentOutOfRangeException(nameof(preference)),
    };

    /// <summary>Возвращает русское название темы для переключателя.</summary>
    /// <param name="preference">Сочетание темы и режима.</param>
    public static string Label(this ClayThemePreference preference) => preference switch
    {
        ClayThemePreference.ClassicLight => "Classic — светлая",
        ClayThemePreference.ClassicDark => "Classic — тёмная",
        ClayThemePreference.LegacyLight => "Legacy",
        ClayThemePreference.CorporateLight => "Corporate",
        ClayThemePreference.ModernLight => "Modern",
        _ => throw new ArgumentOutOfRangeException(nameof(preference)),
    };
}
