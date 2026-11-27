namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки <see cref="ClayDropDownList{TItem, TValue}"/>.</summary>
public sealed class ClayDropDownListOptions : ClayInputOptions
{
    /// <summary>Текст-подсказка в пустом поле.</summary>
    public string? Placeholder { get; set; }

    /// <summary>После подтверждения выбора переводит фокус внутри активной области формы.</summary>
    public bool AdvanceFocusOnSelection { get; set; }
}
