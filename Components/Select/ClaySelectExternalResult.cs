namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Значение, возвращённое формой расширенного поиска в <see cref="ClaySelect{TItem, TValue}"/>.
/// </summary>
/// <typeparam name="TItem">Тип элемента справочника.</typeparam>
/// <typeparam name="TValue">Тип идентификатора элемента.</typeparam>
public sealed class ClaySelectExternalResult<TItem, TValue>
{
    /// <summary>
    /// Идентификатор выбранного элемента.
    /// </summary>
    public required TValue Value { get; init; }

    /// <summary>
    /// Выбранный элемент, если форма может вернуть его целиком.
    /// Если значение не задано, компонент загружает элемент через источник данных.
    /// </summary>
    public TItem? Item { get; init; }
}
