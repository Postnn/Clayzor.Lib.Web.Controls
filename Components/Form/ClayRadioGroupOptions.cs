namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки группы переключателей <see cref="ClayRadioGroup{TValue}"/>.</summary>
public sealed class ClayRadioGroupOptions
{
    /// <summary>Отключает все переключатели.</summary>
    public bool Disabled { get; set; }

    /// <summary>Запрещает изменение выбора без визуального отключения.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Дополнительные CSS-классы группы.</summary>
    public string? Class { get; set; }
}

/// <summary>Типизированный вариант переключателя.</summary>
/// <param name="Value">Сохраняемое значение enum или другое значение поля.</param>
/// <param name="Text">Пользовательская подпись.</param>
public sealed record ClayRadioOption<TValue>(TValue Value, string Text);
