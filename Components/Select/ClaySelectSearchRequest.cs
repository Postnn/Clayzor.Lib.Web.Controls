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
    /// Максимальное количество элементов, которое должен вернуть источник.
    /// Компонент запрашивает на один элемент больше видимого лимита для определения
    /// <see cref="ClaySelectResult{TItem}.HasMore"/>.
    /// </summary>
    public int Limit { get; init; }
}
