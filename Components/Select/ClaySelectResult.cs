namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Результат поиска элементов для <see cref="ClaySelect{TItem, TValue}"/>.
/// </summary>
/// <typeparam name="TItem">Тип элемента справочника. Обязан быть ссылочным типом.</typeparam>
public sealed class ClaySelectResult<TItem>
    where TItem : class
{
    /// <summary>
    /// Найденные элементы в порядке, заданном источником данных.
    /// </summary>
    public IReadOnlyList<TItem> Items { get; init; } = [];

    /// <summary>
    /// Признак того, что источник содержит больше результатов, чем было запрошено.
    /// </summary>
    public bool HasMore { get; init; }
}
