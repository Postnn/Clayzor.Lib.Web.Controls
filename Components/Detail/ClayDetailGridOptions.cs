namespace Clayzor.Lib.Web.Controls.Components.Detail;

/// <summary>Настройки компактной табличной секции формы.</summary>
public sealed class ClayDetailGridOptions
{
    /// <summary>Дополнительные CSS-классы секции.</summary>
    public string? Class { get; set; }

    /// <summary>Текст пустого списка.</summary>
    public string EmptyText { get; set; } = "Нет данных";

    /// <summary>Подсказка кнопки добавления.</summary>
    public string AddTooltip { get; set; } = "Добавить запись";
}
