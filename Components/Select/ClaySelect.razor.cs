using Microsoft.AspNetCore.Components;
using MudBlazor;

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
public partial class ClaySelect<TItem, TValue> : MudFormComponent<TValue, TValue>
    where TItem : class
{
    private readonly string _inputId = $"clay-select-{Guid.NewGuid():N}";
    private readonly string _listboxId = $"clay-select-list-{Guid.NewGuid():N}";
    private ClaySelectOptions _options = new();
    private ElementReference _inputRef;
    private TItem? _currentItem;
    private string _text = string.Empty;
    private TValue? _lastValue;
    private bool _valueSynchronized;
    private IClaySelectDataSource<TItem, TValue>? _lastDataSource;
    private int _resolveVersion;
    private CancellationTokenSource? _resolveCts;

    /// <summary>
    /// Текущие настройки компонента.
    /// </summary>
    protected ClaySelectOptions ResolvedOptions => _options;

    /// <summary>
    /// Текст выбранного элемента или поисковый текст для отображения в поле.
    /// </summary>
    protected string DisplayText => _text;

    /// <summary>
    /// Доступное описание ошибки валидации.
    /// </summary>
    protected string? ValidationMessage => HasErrors ? GetErrorText() : null;

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
    /// Значение, проверяемое родительской <see cref="MudForm"/>.
    /// </summary>
    protected override TValue? ReadValue => Value;

    /// <summary>
    /// Возвращает конвертер без преобразования: компонент работает непосредственно с ID.
    /// </summary>
    protected override IConverter<TValue?, TValue?> GetDefaultConverter() => new EmptyConverter<TValue?>();

    /// <summary>
    /// Устанавливает фокус в поле поиска.
    /// </summary>
    public async Task FocusAsync()
    {
        if (!_options.Disabled)
            await _inputRef.FocusAsync();
    }

    /// <summary>
    /// Очищает выбранные идентификатор и объект, если очистка разрешена настройками.
    /// </summary>
    public Task ClearAsync() => ClearSelectionAsync();

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

        Required = _options.Required;
        RequiredError = _options.RequiredError;
        base.OnParametersSet();
    }

    /// <summary>
    /// Синхронизирует объект с идентификатором, полученным от родительского компонента.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        var valueChanged = !_valueSynchronized ||
                           !EqualityComparer<TValue?>.Default.Equals(_lastValue, Value);
        var sourceChanged = !ReferenceEquals(_lastDataSource, DataSource);

        if (valueChanged || sourceChanged)
        {
            _valueSynchronized = true;
            _lastValue = Value;
            _lastDataSource = DataSource;
            InvalidatePendingOperations();

            if (Value is null)
            {
                _currentItem = null;
                _text = string.Empty;
                return;
            }

            if (SelectedItemMatchesValue())
            {
                _currentItem = SelectedItem;
                _text = TextSelector(SelectedItem!);
                return;
            }

            if (TryFindStaticItem(Value, out var staticItem))
            {
                await ApplyResolvedItemAsync(staticItem!, true);
                return;
            }

            await ResolveItemAsync();
            return;
        }

        if (Value is null)
        {
            _currentItem = null;
            _text = string.Empty;
        }
        else if (_currentItem is null && TryFindStaticItem(Value, out var staticItem))
        {
            await ApplyResolvedItemAsync(staticItem!, true);
        }
        else if (SelectedItemMatchesValue() && !ReferenceEquals(_currentItem, SelectedItem))
        {
            _currentItem = SelectedItem;
            _text = TextSelector(SelectedItem!);
        }
    }

    /// <summary>
    /// Определяет наличие выбранного ID для валидации, игнорируя поисковый текст.
    /// </summary>
    protected override bool HasValue(TValue? value) =>
        value is string text ? !string.IsNullOrWhiteSpace(text) : value is not null;

    /// <summary>
    /// Отменяет незавершённые операции при освобождении компонента.
    /// </summary>
    protected override async ValueTask DisposeAsyncCore()
    {
        InvalidatePendingOperations();
        await base.DisposeAsyncCore();
    }

    private bool SelectedItemMatchesValue() =>
        SelectedItem is not null && Value is not null &&
        EqualityComparer<TValue?>.Default.Equals(ValueSelector(SelectedItem), Value);

    private bool TryFindStaticItem(TValue? value, out TItem? item)
    {
        item = DataSource is null
            ? Items?.FirstOrDefault(candidate =>
                EqualityComparer<TValue?>.Default.Equals(ValueSelector(candidate), value))
            : null;
        return item is not null;
    }

    private async Task ResolveItemAsync()
    {
        if (Value is null)
            return;

        var requestedValue = Value;
        var source = DataSource;
        var previousItem = _currentItem;
        _currentItem = null;
        _text = $"#{requestedValue}";

        if (previousItem is not null)
            await SelectedItemChanged.InvokeAsync(null);

        if (source is null)
            return;

        var version = ++_resolveVersion;
        _resolveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _resolveCts = cts;
        try
        {
            var item = await source.GetByIdAsync((TValue)requestedValue, cts.Token);
            if (version != _resolveVersion || cts.IsCancellationRequested ||
                !EqualityComparer<TValue?>.Default.Equals(Value, requestedValue) ||
                !ReferenceEquals(source, DataSource))
                return;

            if (item is null || !EqualityComparer<TValue?>.Default.Equals(ValueSelector(item), requestedValue))
            {
                await SelectedItemChanged.InvokeAsync(null);
                return;
            }

            await ApplyResolvedItemAsync(item, true);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
    }

    private async Task ApplyResolvedItemAsync(TItem item, bool notify)
    {
        var changed = !ReferenceEquals(_currentItem, item);
        _currentItem = item;
        _text = TextSelector(item);
        if (notify && changed)
            await SelectedItemChanged.InvokeAsync(item);
    }
}
