namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки <see cref="ClayToolbar"/>.</summary>
public sealed class ClayToolbarOptions
{
    /// <summary>Дополнительные CSS-классы.</summary>
    public string? Class { get; set; }

    /// <summary>Использовать компактную высоту панели.</summary>
    public bool Dense { get; set; } = true;
}
