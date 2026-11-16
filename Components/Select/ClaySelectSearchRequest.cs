namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Параметры одного поискового запроса <see cref="ClaySelect{TItem, TValue}"/>.
/// </summary>
public sealed class ClaySelectSearchRequest
{
    /// <summary>
    /// Текст, введённый пользователем. Пустая строка означает начальный список.
    /// </summary>
    public string SearchText { get; init; } = string.Empty;

    /// <summary>
    /// Максимальное количество элементов в <see cref="ClaySelectResult{TItem}.Items"/>.
    /// Источник самостоятельно проверяет наличие дополнительной записи и заполняет
    /// <see cref="ClaySelectResult{TItem}.HasMore"/>.
    /// </summary>
    public int Limit { get; init; }
}
