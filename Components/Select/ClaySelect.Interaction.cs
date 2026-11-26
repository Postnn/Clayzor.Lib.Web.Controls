using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Components.Select;

public partial class ClaySelect<TItem, TValue>
{
    private readonly List<TItem> _results = [];
    private bool _open;
    private bool _loading;
    private bool _hasMore;
    private int _activeIndex = -1;
    private int _searchVersion;
    private CancellationTokenSource? _searchCts;
    private bool _advancedRunning;
    private TItem? _itemBeforeEdit;
    private TValue? _valueBeforeEdit;
    private bool _hasEditBackup;
    private ClaySelectReadError? _readError;

    /// <summary>
    /// Признак открытого списка результатов.
    /// </summary>
    protected bool IsOpen => _open;

    /// <summary>
    /// Признак выполняющегося поиска.
    /// </summary>
    protected bool IsLoading => _loading;

    /// <summary>Признак ожидаемой ошибки Search/GetById с доступным retry.</summary>
    protected bool HasReadError => _readError is not null;

    /// <summary>Повторяет только failed read и никогда не выполняет auto-select по кнопке retry.</summary>
    protected async Task RetryReadAsync()
    {
        var error = _readError;
        if (error is null || _disposed)
            return;
        if (error.Operation == ClaySelectReadOperation.Search)
            await SearchAsync(error.SearchText ?? string.Empty, false);
        else
            await ResolveItemAsync();
    }

    /// <summary>
    /// Признак дополнительных результатов сверх видимого лимита.
    /// </summary>
    protected bool HasMore => _hasMore;

    /// <summary>
    /// Видимые результаты текущего поиска.
    /// </summary>
    protected IReadOnlyList<TItem> Results => _results;

    /// <summary>
    /// Индекс активной строки для клавиатурной навигации.
    /// </summary>
    protected int ActiveIndex => _activeIndex;

    /// <summary>
    /// Идентификатор поля ввода.
    /// </summary>
    protected string InputId => _inputId;

    /// <summary>
    /// Идентификатор списка результатов.
    /// </summary>
    protected string ListboxId => _listboxId;

    /// <summary>
    /// Идентификатор активной строки для связи combobox с экранным диктором.
    /// </summary>
    protected string? ActiveDescendant =>
        _open && !_loading && _activeIndex >= 0 && _activeIndex < _results.Count
            ? GetOptionId(_activeIndex)
            : null;

    /// <summary>
    /// Возвращает идентификатор строки результата.
    /// </summary>
    protected string GetOptionId(int index) => $"{_listboxId}-option-{index}";

    /// <summary>
    /// Признак отображения пункта расширенного поиска.
    /// </summary>
    protected bool ShowAdvancedSearch =>
        AdvancedSearch is not null &&
        (_options.AlwaysShowAdvancedSearch || _hasMore || (!_loading && _results.Count == 0));

    /// <summary>
    /// Признак доступности изменений пользователем.
    /// </summary>
    protected bool IsMutable => !_options.Disabled && !_options.ReadOnly;

    /// <summary>
    /// Показывает кнопку просмотра вместо поиска, если текст выбранного элемента не изменён.
    /// </summary>
    protected bool IsViewMode => ViewItem is not null && HasCurrentSelectionText();

    /// <summary>
    /// Изменяет поисковый текст без запуска запроса. Поиск выполняется по Enter,
    /// при потере фокуса или нажатию кнопки с многоточием.
    /// </summary>
    protected async Task HandleInputAsync(ChangeEventArgs args)
    {
        if (!IsMutable)
            return;

        InvalidateSearchResults();
        var searchText = args.Value?.ToString() ?? string.Empty;
        if (Value is not null && _options.Clearable)
        {
            if (!_hasEditBackup)
            {
                _itemBeforeEdit = _currentItem;
                _valueBeforeEdit = Value;
                _hasEditBackup = true;
            }
            await ClearSelectionAsync();
        }

        _text = searchText;
    }

    /// <summary>
    /// Открывает карточку текущего элемента без изменения выбора или поискового текста.
    /// </summary>
    protected async Task OpenViewItemAsync()
    {
        if (!IsMutable || _currentItem is null || ViewItem is null || !HasCurrentSelectionText())
            return;

        CancelSearch();
        _open = false;
        await ViewItem(_currentItem);
    }

    /// <summary>
    /// Выполняет поиск введённого текста или закрывает открытый список.
    /// </summary>
    protected async Task TogglePopupAsync()
    {
        if (!IsMutable || _advancedRunning)
            return;

        if (_open)
        {
            ClosePopupAndRestoreText();
            return;
        }

        await SearchAsync(_text, false);
    }

    /// <summary>
    /// Выбирает элемент, если он всё ещё принадлежит актуальной видимой выдаче.
    /// </summary>
    protected async Task SelectResultAsync(TItem item)
    {
        if (_open && !_loading && _results.Contains(item))
            await SelectItemAsync(item);
    }

    /// <summary>Идентификатор кнопки расширенного поиска.</summary>
    protected string AdvancedActionId => $"{_listboxId}-advanced";

    /// <summary>Идентификатор кнопки создания элемента.</summary>
    protected string CreateActionId => $"{_listboxId}-create";

    /// <summary>
    /// Выполняет поиск при потере фокуса без открытия popup и проверяет выбранное значение.
    /// </summary>
    protected async Task HandleBlurAsync()
    {
        if (_advancedRunning || _disposed)
            return;

        var resolveVersion = _resolveVersion;
        var searchVersion = _searchVersion;
        var searchText = _text;
        if (_open && await JS.InvokeAsync<bool>("claySelect.containsFocus", _inputId))
            return;
        if (resolveVersion != _resolveVersion || searchVersion != _searchVersion ||
            _advancedRunning || _disposed)
            return;

        CancelSearch();
        _open = false;
        _activeIndex = -1;
        Touched = true;
        if (IsMutable)
            await SearchAsync(searchText, true, false);
        if (_hasEditBackup && Value is null && string.IsNullOrWhiteSpace(_text))
        {
            _hasEditBackup = false;
            _itemBeforeEdit = null;
            _valueBeforeEdit = default;
            await Cleared.InvokeAsync();
        }
        await BeginValidateAsync();
    }

    /// <summary>
    /// Обрабатывает клавиатурную навигацию, запуск поиска по Enter и выбор результата.
    /// </summary>
    protected async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (!IsMutable || _advancedRunning)
            return;

        if (_loading && args.Key is "ArrowDown" or "ArrowUp" or "Enter")
            return;

        switch (args.Key)
        {
            case "ArrowDown":
                if (_open && _results.Count > 0)
                {
                    _activeIndex = Math.Min(_activeIndex + 1, _results.Count - 1);
                    await EnsureVisibleAsync(GetOptionId(_activeIndex));
                }
                break;
            case "ArrowUp":
                if (_open && _results.Count > 0)
                {
                    _activeIndex = Math.Max(_activeIndex - 1, 0);
                    await EnsureVisibleAsync(GetOptionId(_activeIndex));
                }
                break;
            case "Enter":
                if (_open && _activeIndex >= 0 && _activeIndex < _results.Count)
                    await SelectItemAsync(_results[_activeIndex]);
                else if (!_open && !HasCurrentSelectionText())
                    await SearchAsync(_text, true);
                break;
            case "Escape":
                await RestorePreviousSelectionAsync();
                break;
            case "Tab":
                if (_loading || !(ShowCreateItem || ShowAdvancedSearch))
                {
                    CancelSearch();
                    _open = false;
                    _activeIndex = -1;
                }
                break;
        }
    }

    private async Task HandlePopupKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key != "Escape" || !IsMutable || _advancedRunning)
            return;

        await RestorePreviousSelectionAsync();
        await FocusAsync();
    }

    private async Task EnsureVisibleAsync(string elementId)
    {
        if (!_disposed)
            await JS.InvokeVoidAsync("claySelect.ensureVisible", _inputId, elementId);
    }

    /// <summary>
    /// Вызывает расширенный поиск и применяет возвращённый объект или идентификатор.
    /// Текст уже выбранного элемента не передаётся как новый поисковый запрос.
    /// </summary>
    protected async Task OpenAdvancedSearchAsync()
    {
        if (!IsMutable || _advancedRunning || AdvancedSearch is null || _disposed)
            return;

        _advancedRunning = true;
        CancelSearch();
        _open = false;
        var version = _resolveVersion;
        var appliedLegacyResult = false;
        try
        {
            var result = await AdvancedSearch(new ClaySelectAdvancedSearchContext
            {
                SearchText = HasCurrentSelectionText() ? string.Empty : _text,
                ApplyResult = (Func<ClaySelectExternalResult<TItem, TValue>, CancellationToken, Task<ClaySelectReturnDecision>>)
                    ((result, token) => ApplyAcknowledgedResultAsync(result, version, token)),
            });

            if (result is null || version != _resolveVersion || !IsMutable || _disposed)
                return;

            if (result.ContainsItem)
                await SelectItemAsync(result.Item!);
            else
                await ApplyExternalValueAsync(result.Value);
            appliedLegacyResult = true;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _advancedRunning = false;
            if (!_disposed && (appliedLegacyResult || version == _resolveVersion))
            {
                if (version == _resolveVersion)
                    RestoreSelectedText();
                await FocusAsync();
            }
        }
    }

    private async Task<ClaySelectReturnDecision> ApplyAcknowledgedResultAsync(
        ClaySelectExternalResult<TItem, TValue> result, int version, CancellationToken token)
    {
        var decision = ClaySelectReturnDecision.Stale;
        await InvokeAsync(async () =>
        {
            bool Current() => !token.IsCancellationRequested && !_disposed && IsMutable && version == _resolveVersion;
            if (!Current())
                return;

            var item = result.Item;
            if (!result.ContainsItem && result.Value is { } value)
            {
                if (!TryFindStaticItem(value, out item) && DataSource is { } source)
                {
                    try { item = await source.GetByIdAsync(value, token); }
                    catch (Exception ex) when (Current())
                    {
                        _readError = new ClaySelectReadError(ClaySelectReadOperation.Resolve, ex, null);
                        await ReadError.InvokeAsync(_readError);
                        StateHasChanged();
                        decision = ClaySelectReturnDecision.Rejected;
                        return;
                    }
                    catch (OperationCanceledException) when (!Current()) { return; }
                }

                if (item is not null && !EqualityComparer<TValue?>.Default.Equals(ValueSelector(item), value))
                    item = null;
            }

            if (!Current())
                return;
            if (item is null)
            {
                decision = ClaySelectReturnDecision.Rejected;
                return;
            }

            var id = ValueSelector(item);
            await SelectItemAsync(item);
            if (!token.IsCancellationRequested && !_disposed && IsMutable
                && _resolveVersion == version + 1 && EqualityComparer<TValue?>.Default.Equals(Value, id))
            {
                decision = ClaySelectReturnDecision.Accepted;
                StateHasChanged();
            }
        });

        return decision;
    }

    /// <summary>Признак отображения пункта создания элемента.</summary>
    protected bool ShowCreateItem => CreateItem is not null && IsMutable;

    /// <summary>Открывает внешнюю форму создания и применяет возвращённый элемент.</summary>
    protected async Task OpenCreateItemAsync()
    {
        if (!IsMutable || _advancedRunning || CreateItem is null || _disposed)
            return;

        _advancedRunning = true;
        CancelSearch();
        _open = false;
        var version = _resolveVersion;
        try
        {
            var result = await CreateItem(new ClaySelectAdvancedSearchContext
            {
                SearchText = HasCurrentSelectionText() ? string.Empty : _text,
            });

            if (result is null || version != _resolveVersion || !IsMutable || _disposed)
                return;

            if (result.ContainsItem)
                await SelectItemAsync(result.Item!);
            else
                await ApplyExternalValueAsync(result.Value);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _advancedRunning = false;
            if (!_disposed)
            {
                if (version == _resolveVersion)
                    RestoreSelectedText();
                await FocusAsync();
            }
        }
    }

    private async Task SearchAsync(string searchText, bool allowAutoSelect, bool openPopup = true)
    {
        if (!IsMutable || _disposed)
            return;

        CancelSearch();
        _readError = null;
        var version = _searchVersion;
        if (searchText.Length < _options.MinSearchLength)
        {
            _open = false;
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        if (openPopup)
        {
            await JS.InvokeVoidAsync("claySelect.closeOthers", _inputId);
            if (!IsCurrentSearch(version, cts))
                return;
        }
        _open = openPopup;
        _loading = true;
        _activeIndex = -1;
        await InvokeAsync(StateHasChanged);
        if (!IsCurrentSearch(version, cts))
            return;

        ClaySelectResult<TItem> result;
        try
        {
            if (DataSource is not null)
            {
                result = await DataSource.SearchAsync(new ClaySelectSearchRequest
                {
                    SearchText = searchText,
                    Limit = _options.MaxItems,
                }, cts.Token);
            }
            else
            {
                result = SearchStatic(searchText);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!IsCurrentSearch(version, cts))
                return;
            _readError = new ClaySelectReadError(ClaySelectReadOperation.Search, ex, searchText);
            await ReadError.InvokeAsync(_readError);
            return;
        }
        finally
        {
            if (version == _searchVersion)
                _loading = false;
        }

        if (!IsCurrentSearch(version, cts))
            return;

        _results.Clear();
        _results.AddRange(result.Items.Take(_options.MaxItems));
        _hasMore = result.HasMore || result.Items.Count > _options.MaxItems;
        _activeIndex = _results.Count > 0 ? 0 : -1;

        if (allowAutoSelect && !string.IsNullOrWhiteSpace(searchText) && !_hasMore &&
            TryGetAutoSelection(searchText, out var selected))
            await SelectItemAsync(selected!);
    }

    private bool IsCurrentSearch(int version, CancellationTokenSource cts) =>
        version == _searchVersion && !cts.IsCancellationRequested && !_disposed && IsMutable;

    private bool HasCurrentSelectionText() =>
        Value is not null && _currentItem is not null &&
        string.Equals(_text, TextSelector(_currentItem), StringComparison.Ordinal);

    private ClaySelectResult<TItem> SearchStatic(string searchText)
    {
        var query = Items ?? Enumerable.Empty<TItem>();
        if (!string.IsNullOrWhiteSpace(searchText))
            query = query.Where(item => TextSelector(item).Contains(searchText, StringComparison.OrdinalIgnoreCase));

        var matches = query.Take(_options.MaxItems + 1).ToList();
        return new ClaySelectResult<TItem>
        {
            Items = matches.Take(_options.MaxItems).ToList(),
            HasMore = matches.Count > _options.MaxItems,
        };
    }

    private bool TryGetAutoSelection(string searchText, out TItem? selected)
    {
        selected = null;
        if (_options.AutoSelectSingle && _results.Count == 1)
        {
            selected = _results[0];
            return true;
        }

        if (!_options.AutoSelectExactMatch)
            return false;

        var matches = _results
            .Where(item => string.Equals(TextSelector(item).Trim(), searchText.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
            return false;

        var firstValue = ValueSelector(matches[0]);
        if (matches.Any(item => !EqualityComparer<TValue>.Default.Equals(ValueSelector(item), firstValue)))
            return false;

        selected = matches[0];
        return true;
    }

    private async Task SelectItemAsync(TItem item)
    {
        if (!IsMutable)
            return;

        var value = ValueSelector(item);
        if (value is null)
            throw new InvalidOperationException("ValueSelector вернул null для выбранного элемента.");

        _hasEditBackup = false;
        _itemBeforeEdit = null;
        _valueBeforeEdit = default;
        InvalidatePendingOperations();
        Value = value;
        _lastValue = value;
        _valueSynchronized = true;
        _currentItem = item;
        _text = TextSelector(item);
        await ValueChanged.InvokeAsync(value);
        await SelectedItemChanged.InvokeAsync(item);
        await NotifyUserValidationAsync();
    }

    private async Task RestorePreviousSelectionAsync()
    {
        if (!_hasEditBackup)
        {
            ClosePopupAndRestoreText();
            return;
        }

        var item = _itemBeforeEdit;
        var value = _valueBeforeEdit;
        if (item is not null)
            await SelectItemAsync(item);
        else
            await ApplyExternalValueAsync(value);
    }

    private async Task ClearSelectionAsync()
    {
        if (!IsMutable || !_options.Clearable || (Value is null && _currentItem is null))
            return;

        InvalidatePendingOperations();
        Value = default;
        _lastValue = default;
        _valueSynchronized = true;
        _currentItem = null;
        _text = string.Empty;
        await ValueChanged.InvokeAsync(default(TValue));
        await SelectedItemChanged.InvokeAsync(null);
        await NotifyUserValidationAsync();
    }

    private async Task ApplyExternalValueAsync(TValue? value)
    {
        if (value is null || !IsMutable)
            return;

        _hasEditBackup = false;
        _itemBeforeEdit = null;
        _valueBeforeEdit = default;
        InvalidatePendingOperations();
        Value = value;
        _lastValue = value;
        _valueSynchronized = true;
        await ValueChanged.InvokeAsync(value);

        if (TryFindStaticItem(value, out var item))
            await ApplyResolvedItemAsync(item!, true);
        else
            await ResolveItemAsync();

        await NotifyUserValidationAsync();
    }

    private async Task NotifyUserValidationAsync()
    {
        Touched = true;
        FieldChanged(Value);
        await BeginValidateAsync();
    }

    private void ClosePopupAndRestoreText()
    {
        CancelSearch();
        _open = false;
        RestoreSelectedText();
    }

    private void RestoreSelectedText()
    {
        _text = _currentItem is not null
            ? TextSelector(_currentItem)
            : Value is null ? string.Empty : $"#{Value}";
    }

    private void CancelSearch()
    {
        _searchVersion++;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _loading = false;
    }

    private void InvalidateSearchResults()
    {
        CancelSearch();
        _readError = null;
        _results.Clear();
        _hasMore = false;
        _activeIndex = -1;
        _open = false;
    }

    private void InvalidatePendingOperations()
    {
        CancelSearch();
        _readError = null;
        _resolveVersion++;
        _resolveCts?.Cancel();
        _resolveCts?.Dispose();
        _resolveCts = null;
        _open = false;
        _activeIndex = -1;
    }
}
