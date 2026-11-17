namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки <see cref="ClayChanged"/>.</summary>
public sealed class ClayChangedOptions
{
    /// <summary>Подпись перед сведениями об изменении.</summary>
    public string Label { get; set; } = "Изменил:";

    /// <summary>Формат даты и времени изменения.</summary>
    public string DateTimeFormat { get; set; } = "dd.MM.yyyy HH:mm:ss";

    /// <summary>Дополнительные CSS-классы.</summary>
    public string? Class { get; set; }
}
