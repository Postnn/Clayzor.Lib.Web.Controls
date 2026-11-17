namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки <see cref="ClayTextBox"/>.</summary>
public sealed class ClayTextBoxOptions : ClayInputOptions
{
    /// <summary>Максимальная длина значения; 0 снимает ограничение.</summary>
    public int MaxLength { get; set; }

    /// <summary>Текст-подсказка в пустом поле.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Передавать изменения при вводе, а не только при потере фокуса.</summary>
    public bool Immediate { get; set; } = true;
}
