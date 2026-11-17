namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки <see cref="ClayToolbarButton"/>.</summary>
public sealed class ClayToolbarButtonOptions
{
    /// <summary>Текст кнопки.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Иконка перед текстом.</summary>
    public string? Icon { get; set; }

    /// <summary>Текст всплывающей подсказки.</summary>
    public string? Title { get; set; }

    /// <summary>Блокировка кнопки.</summary>
    public bool Disabled { get; set; }

    /// <summary>Цвет кнопки.</summary>
    public MudBlazor.Color Color { get; set; } = MudBlazor.Color.Default;

    /// <summary>Вариант оформления кнопки.</summary>
    public MudBlazor.Variant Variant { get; set; } = MudBlazor.Variant.Outlined;
}
