namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Источник элементов для <see cref="ClaySelect{TItem, TValue}"/>.
/// </summary>
/// <typeparam name="TItem">Тип элемента справочника. Обязан быть ссылочным типом.</typeparam>
/// <typeparam name="TValue">
/// Nullable-тип идентификатора: например, <c>int?</c>, <c>Guid?</c> или <c>string</c>.
/// Только <see langword="null"/> означает пустой выбор.
/// </typeparam>
public interface IClaySelectDataSource<TItem, TValue>
    where TItem : class
{
    /// <summary>
    /// Выполняет поиск элементов по тексту и прикладным ограничениям источника.
    /// </summary>
    /// <param name="request">Параметры поискового запроса.</param>
    /// <param name="cancellationToken">Токен отмены устаревшего запроса.</param>
    /// <returns>Найденные элементы и признак наличия дополнительных результатов.</returns>
    Task<ClaySelectResult<TItem>> SearchAsync(
        ClaySelectSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает элемент по идентификатору для восстановления отображаемого значения.
    /// </summary>
    /// <param name="id">Идентификатор элемента.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Найденный элемент или <see langword="null"/>.</returns>
    Task<TItem?> GetByIdAsync(TValue id, CancellationToken cancellationToken = default);
}
