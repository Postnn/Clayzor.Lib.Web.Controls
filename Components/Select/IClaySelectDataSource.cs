namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Источник элементов для <see cref="ClaySelect{TItem, TValue}"/>.
/// </summary>
/// <typeparam name="TItem">Тип элемента справочника.</typeparam>
/// <typeparam name="TValue">Тип идентификатора элемента.</typeparam>
public interface IClaySelectDataSource<TItem, TValue>
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
