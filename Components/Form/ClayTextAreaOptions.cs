namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки <see cref="ClayTextArea"/>.</summary>
public sealed class ClayTextAreaOptions : ClayInputOptions
{
    /// <summary>Максимальная длина значения; 0 снимает ограничение.</summary>
    public int MaxLength { get; set; }

    /// <summary>Количество видимых строк.</summary>
    public int Lines { get; set; } = 3;

    /// <summary>Текст-подсказка в пустом поле.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Передавать изменения при вводе, а не только при потере фокуса.</summary>
    public bool Immediate { get; set; } = true;

    /// <summary>Отключает .NET input/change и оставляет нативное редактирование внешнему браузерному обработчику.</summary>
    public bool BrowserManaged { get; set; }

    /// <summary>Доступное название браузерного поля без видимой подписи.</summary>
    public string? BrowserLabel { get; set; }
}
