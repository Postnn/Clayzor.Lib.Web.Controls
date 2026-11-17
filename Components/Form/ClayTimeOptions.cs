namespace Clayzor.Lib.Web.Controls.Components.Form;

/// <summary>Настройки <see cref="ClayTime"/>.</summary>
public sealed class ClayTimeOptions : ClayInputOptions
{
    /// <summary>Формат отображения времени.</summary>
    public string TimeFormat { get; set; } = "HH:mm:ss";

    /// <summary>Разрешает ручной ввод времени.</summary>
    public bool Editable { get; set; } = true;

    /// <summary>Показывает действие очистки значения.</summary>
    public bool Clearable { get; set; } = true;
}
