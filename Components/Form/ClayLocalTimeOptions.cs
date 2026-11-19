namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки отображения UTC-даты в локальном времени браузера.</summary>
public sealed class ClayLocalTimeOptions
{
    /// <summary>Формат отображаемой даты и времени.</summary>
    public string DateTimeFormat { get; set; } = "dd.MM.yyyy HH:mm:ss";

    /// <summary>Дополнительные CSS-классы.</summary>
    public string? Class { get; set; }
}
