namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>
/// Общие настройки контролов ввода форм Clayzor.
/// </summary>
public abstract class ClayInputOptions
{
    /// <summary>Подпись поля.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Признак обязательного заполнения.</summary>
    public bool Required { get; set; }

    /// <summary>Текст ошибки обязательного поля.</summary>
    public string RequiredError { get; set; } = "Заполните поле";

    /// <summary>Отключает взаимодействие с полем.</summary>
    public bool Disabled { get; set; }

    /// <summary>Запрещает изменение значения без отключения поля.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Дополнительные CSS-классы.</summary>
    public string? Class { get; set; }

    /// <summary>Стабильный ключ восстановления незавершённого ввода DR-01.</summary>
    public string? DraftKey { get; set; }

    /// <summary>Вариант оформления поля MudBlazor.</summary>
    public MudBlazor.Variant Variant { get; set; } = MudBlazor.Variant.Outlined;

    /// <summary>Плотность поля MudBlazor.</summary>
    public MudBlazor.Margin Margin { get; set; } = MudBlazor.Margin.Dense;
}
