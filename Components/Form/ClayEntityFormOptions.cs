namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки общего контейнера отдельной формы редактирования.</summary>
public sealed class ClayEntityFormOptions
{
    /// <summary>Максимальная ширина формы.</summary>
    public string MaxWidth { get; set; } = "100%";

    /// <summary>Дополнительные CSS-классы содержимого формы.</summary>
    public string? Class { get; set; }

    /// <summary>Уровень тени контейнера.</summary>
    public int Elevation { get; set; } = 1;
}
