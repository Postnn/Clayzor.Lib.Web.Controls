using Microsoft.AspNetCore.Components;

namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Поле одиночного выбора элемента из статического списка или удалённого источника данных.
/// </summary>
/// <typeparam name="TItem">Тип элемента справочника.</typeparam>
/// <typeparam name="TValue">Тип идентификатора элемента.</typeparam>
public partial class ClaySelect<TItem, TValue>
{
    private ClaySelectOptions _options = new();

    /// <summary>
    /// Текущие настройки компонента.
    /// </summary>
    protected ClaySelectOptions ResolvedOptions => _options;

    /// <summary>
    /// Текст выбранного элемента для отображения в поле.
    /// </summary>
    protected string DisplayText => SelectedItem is null ? string.Empty : TextSelector(SelectedItem);

    /// <summary>
    /// Выбранный идентификатор. Значение по умолчанию типа означает пустой выбор.
    /// </summary>
    [Parameter]
    public TValue? Value { get; set; }

    /// <summary>
    /// Событие изменения выбранного идентификатора.
    /// </summary>
    [Parameter]
    public EventCallback<TValue?> ValueChanged { get; set; }

    /// <summary>
    /// Выбранный элемент целиком.
    /// </summary>
    [Parameter]
    public TItem? SelectedItem { get; set; }

    /// <summary>
    /// Событие изменения выбранного элемента.
    /// </summary>
    [Parameter]
    public EventCallback<TItem?> SelectedItemChanged { get; set; }

    /// <summary>
    /// Статические элементы. Используются, если не задан <see cref="DataSource"/>.
    /// </summary>
    [Parameter]
    public IEnumerable<TItem>? Items { get; set; }

    /// <summary>
    /// Источник серверного поиска и восстановления элемента по идентификатору.
    /// </summary>
    [Parameter]
    public IClaySelectDataSource<TItem, TValue>? DataSource { get; set; }

    /// <summary>
    /// Функция получения идентификатора элемента.
    /// </summary>
    [Parameter, EditorRequired]
    public required Func<TItem, TValue> ValueSelector { get; set; }

    /// <summary>
    /// Функция получения отображаемого текста элемента.
    /// </summary>
    [Parameter, EditorRequired]
    public required Func<TItem, string> TextSelector { get; set; }

    /// <summary>
    /// Настройки внешнего вида и поведения компонента.
    /// </summary>
    [Parameter]
    public ClaySelectOptions? Options { get; set; }

    /// <summary>
    /// Шаблон строки результата поиска. Если не задан, отображается только текст элемента.
    /// </summary>
    [Parameter]
    public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>
    /// Открывает форму расширенного поиска и возвращает выбранное значение.
    /// Возврат <see langword="null"/> означает отмену без изменения текущего значения.
    /// </summary>
    [Parameter]
    public Func<ClaySelectAdvancedSearchContext, Task<ClaySelectExternalResult<TItem, TValue>?>>? AdvancedSearch { get; set; }

    /// <summary>
    /// Применяет настройки, переданные страницей.
    /// </summary>
    protected override void OnParametersSet()
    {
        _options = Options ?? new ClaySelectOptions();
    }
}
