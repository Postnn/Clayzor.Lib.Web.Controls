using Microsoft.AspNetCore.Components;

namespace Clayzor.Lib.Web.Controls.Components.Select;

/// <summary>
/// Поле одиночного выбора элемента из статического списка или удалённого источника данных.
/// </summary>
/// <remarks>
/// <para>
/// <c>Value</c> — источник истины. При его внешнем изменении компонент синхронизирует
/// <c>SelectedItem</c>, даже если родитель передал объект с прежним идентификатором.
/// Пустой <c>Value</c> очищает объект и текст. При ненайденном идентификаторе значение
/// сохраняется, объект очищается, а поле показывает техническое представление идентификатора.
/// </para>
/// <para>
/// <c>SelectedItemChanged</c> вызывается и после восстановления объекта по неизменному ID;
/// в этом случае повторный <c>ValueChanged</c> не вызывается.
/// </para>
/// </remarks>
/// <typeparam name="TItem">Тип элемента справочника. Обязан быть ссылочным типом.</typeparam>
/// <typeparam name="TValue">
/// Nullable-тип идентификатора: например, <c>int?</c>, <c>Guid?</c> или <c>string</c>.
/// Для значимых ID потребитель должен использовать <c>Nullable&lt;T&gt;</c>; только
/// <see langword="null"/> означает пустой выбор, а 0 и <see cref="Guid.Empty"/> допустимы.
/// </typeparam>
public partial class ClaySelect<TItem, TValue>
    where TItem : class
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
    /// Выбранный идентификатор и источник истины компонента. Только <see langword="null"/>
    /// означает пустой выбор; <typeparamref name="TValue"/> сам должен допускать null.
    /// </summary>
    [Parameter]
    public TValue? Value { get; set; }

    /// <summary>
    /// Событие изменения выбранного идентификатора.
    /// </summary>
    [Parameter]
    public EventCallback<TValue?> ValueChanged { get; set; }

    /// <summary>
    /// Объект, соответствующий <see cref="Value"/>. При расхождении приоритет имеет идентификатор.
    /// </summary>
    [Parameter]
    public TItem? SelectedItem { get; set; }

    /// <summary>
    /// Событие изменения объекта, включая его восстановление по ID без смены самого ID.
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
    /// Имеет приоритет над <see cref="Items"/>.
    /// </summary>
    [Parameter]
    public IClaySelectDataSource<TItem, TValue>? DataSource { get; set; }

    /// <summary>
    /// Функция получения непустого идентификатора элемента.
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
    /// Пункт отображается только при заданном обработчике и доступен лишь в изменяемом состоянии.
    /// </summary>
    [Parameter]
    public Func<ClaySelectAdvancedSearchContext, Task<ClaySelectExternalResult<TItem, TValue>?>>? AdvancedSearch { get; set; }

    /// <summary>
    /// Проверяет nullable-тип идентификатора и допустимость настроек страницы.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TValue"/> является неnullable значимым типом.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Заданы недопустимые числовые настройки.</exception>
    protected override void OnParametersSet()
    {
        if (default(TValue) is not null)
            throw new InvalidOperationException(
                $"Тип идентификатора {typeof(TValue).Name} должен допускать null. Используйте Nullable<T> или ссылочный тип.");

        _options = Options ?? new ClaySelectOptions();
        if (_options.MaxItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(Options), "MaxItems должен быть больше нуля.");
        if (_options.DebounceInterval < 0)
            throw new ArgumentOutOfRangeException(nameof(Options), "DebounceInterval не может быть отрицательным.");
        if (_options.MinSearchLength < 0)
            throw new ArgumentOutOfRangeException(nameof(Options), "MinSearchLength не может быть отрицательным.");
    }
}
