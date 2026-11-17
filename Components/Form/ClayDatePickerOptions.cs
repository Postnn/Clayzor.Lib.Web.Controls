namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки <see cref="ClayDatePicker"/>.</summary>
public sealed class ClayDatePickerOptions : ClayInputOptions
{
    /// <summary>Минимально допустимая дата.</summary>
    public DateTime? MinDate { get; set; }

    /// <summary>Максимально допустимая дата.</summary>
    public DateTime? MaxDate { get; set; }

    /// <summary>Формат отображения даты.</summary>
    public string DateFormat { get; set; } = "dd.MM.yyyy";

    /// <summary>Разрешает ручной ввод даты.</summary>
    public bool Editable { get; set; } = true;

    /// <summary>Показывает действие очистки значения.</summary>
    public bool Clearable { get; set; } = true;
}
