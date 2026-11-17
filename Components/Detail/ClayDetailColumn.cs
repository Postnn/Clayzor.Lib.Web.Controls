namespace Clayzor.Lib.Web.Controls.Components.Detail;

/// <summary>Описание текстовой колонки компактной табличной секции.</summary>
/// <typeparam name="TItem">Тип строки секции.</typeparam>
public sealed class ClayDetailColumn<TItem>
{
    /// <summary>Заголовок колонки.</summary>
    public required string Title { get; init; }

    /// <summary>Формирование отображаемого текста строки.</summary>
    public required Func<TItem, string?> TextSelector { get; init; }
}
