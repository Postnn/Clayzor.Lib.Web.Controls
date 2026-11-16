namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Контекст вызова расширенного поиска из <see cref="ClaySelect{TItem, TValue}"/>.
/// </summary>
public sealed class ClaySelectAdvancedSearchContext
{
    /// <summary>
    /// Текущий поисковый текст, который можно передать в открываемую форму.
    /// </summary>
    public string SearchText { get; init; } = string.Empty;
}
