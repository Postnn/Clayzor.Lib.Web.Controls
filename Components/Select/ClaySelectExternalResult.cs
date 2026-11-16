namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Непротиворечивый результат формы расширенного поиска: либо полный элемент,
/// либо его идентификатор. Экземпляры создаются через <see cref="FromItem"/> или
/// <see cref="FromValue"/>.
/// </summary>
/// <typeparam name="TItem">Тип элемента справочника. Обязан быть ссылочным типом.</typeparam>
/// <typeparam name="TValue">Nullable-тип идентификатора элемента.</typeparam>
public sealed class ClaySelectExternalResult<TItem, TValue>
    where TItem : class
{
    private ClaySelectExternalResult()
    {
    }

    /// <summary>
    /// Признак того, что результат содержит полный элемент.
    /// </summary>
    public bool ContainsItem { get; private init; }

    /// <summary>
    /// Полный элемент, если результат создан через <see cref="FromItem"/>.
    /// </summary>
    public TItem? Item { get; private init; }

    /// <summary>
    /// Идентификатор, если результат создан через <see cref="FromValue"/>.
    /// </summary>
    public TValue? Value { get; private init; }

    /// <summary>
    /// Создаёт результат с полным элементом. Идентификатор получается через
    /// <c>ValueSelector</c> компонента.
    /// </summary>
    /// <param name="item">Выбранный элемент.</param>
    /// <returns>Результат, не требующий дополнительной загрузки элемента.</returns>
    public static ClaySelectExternalResult<TItem, TValue> FromItem(TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ClaySelectExternalResult<TItem, TValue>
        {
            ContainsItem = true,
            Item = item,
        };
    }

    /// <summary>
    /// Создаёт результат с идентификатором. Компонент восстанавливает элемент через
    /// источник данных или статический список.
    /// </summary>
    /// <param name="value">Непустой идентификатор выбранного элемента.</param>
    /// <returns>Результат с идентификатором выбранного элемента.</returns>
    public static ClaySelectExternalResult<TItem, TValue> FromValue(TValue value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        return new ClaySelectExternalResult<TItem, TValue>
        {
            Value = value,
        };
    }
}
