using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

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

    /// <summary>
    /// Признак открытого списка результатов.
    /// </summary>
    protected bool IsOpen => _open;

    /// <summary>
    /// Признак выполняющегося поиска.
    /// </summary>
    protected bool IsLoading => _loading;

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
    /// Обрабатывает изменение поискового текста.
    /// </summary>
    protected async Task HandleInputAsync(ChangeEventArgs args)
    {
        if (!IsMutable)
            return;

        var searchText = args.Value?.ToString() ?? string.Empty;
        if (Value is not null && _options.Clearable)
            await ClearSelectionAsync();

        _text = searchText;
        await QueueSearchAsync(searchText);
    }

    /// <summary>
    /// Открывает или закрывает список по нажатию кнопки с многоточием.
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

        await SearchAsync(string.Empty, false);
    }

    /// <summary>
    /// Выбирает элемент из списка результатов.
    /// </summary>
    protected Task SelectResultAsync(TItem item) => SelectItemAsync(item);

    /// <summary>
    /// Обрабатывает потерю фокуса полем.
    /// </summary>
    protected async Task HandleBlurAsync()
    {
        if (_advancedRunning)
            return;

        ClosePopupAndRestoreText();
        Touched = true;
        await BeginValidateAsync();
    }

    /// <summary>
    /// Обрабатывает клавиатурную навигацию и выбор.
    /// </summary>
    protected async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (!IsMutable || _advancedRunning)
            return;

        switch (args.Key)
        {
            case "ArrowDown":
                if (!_open)
                    await SearchAsync(string.Empty, false);
                if (_results.Count > 0)
                    _activeIndex = Math.Min(_activeIndex + 1, _results.Count - 1);
                break;
            case "ArrowUp":
                if (_open && _results.Count > 0)
                    _activeIndex = Math.Max(_activeIndex - 1, 0);
                break;
            case "Enter":
                if (_open && _activeIndex >= 0 && _activeIndex < _results.Count)
                    await SelectItemAsync(_results[_activeIndex]);
                else if (!_open)
                    await SearchAsync(string.Empty, false);
                break;
            case "Escape":
            case "Tab":
                ClosePopupAndRestoreText();
                break;
        }
    }

    /// <summary>
    /// Вызывает расширенный поиск и применяет возвращённый объект или идентификатор.
    /// </summary>
    protected async Task OpenAdvancedSearchAsync()
    {
        if (!IsMutable || _advancedRunning || AdvancedSearch is null)
            return;

        _advancedRunning = true;
        CancelSearch();
        _open = false;
        var version = _resolveVersion;
        try
        {
            var result = await AdvancedSearch(new ClaySelectAdvancedSearchContext
            {
                SearchText = _text,
            });

            if (result is null || version != _resolveVersion || !IsMutable)
                return;

            if (result.ContainsItem)
                await SelectItemAsync(result.Item!);
            else
                await ApplyExternalValueAsync(result.Value);
        }
        finally
        {
            _advancedRunning = false;
            if (version == _resolveVersion)
                RestoreSelectedText();
            await FocusAsync();
        }
    }

    private async Task QueueSearchAsync(string searchText)
    {
        CancelSearch();
        var version = _searchVersion;
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        try
        {
            if (_options.DebounceInterval > 0)
                await Task.Delay(_options.DebounceInterval, cts.Token);
            if (version != _searchVersion || searchText != _text)
                return;
            await SearchAsync(searchText, true);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
    }

    private async Task SearchAsync(string searchText, bool allowAutoSelect)
    {
        if (!IsMutable)
            return;

        CancelSearch();
        var version = _searchVersion;
        if (searchText.Length < _options.MinSearchLength)
        {
            _open = false;
            _results.Clear();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _open = true;
        _loading = true;
        _hasMore = false;
        _activeIndex = -1;
        await InvokeAsync(StateHasChanged);

        try
        {
            ClaySelectResult<TItem> result;
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

            if (version != _searchVersion || cts.IsCancellationRequested)
                return;

            _results.Clear();
            _results.AddRange(result.Items.Take(_options.MaxItems));
            _hasMore = result.HasMore || result.Items.Count > _options.MaxItems;
            _activeIndex = _results.Count > 0 ? 0 : -1;

            if (allowAutoSelect && !string.IsNullOrWhiteSpace(searchText) && !_hasMore &&
                TryGetAutoSelection(searchText, out var selected))
                await SelectItemAsync(selected!);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            if (version == _searchVersion)
                _loading = false;
        }
    }

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

    private void InvalidatePendingOperations()
    {
        CancelSearch();
        _resolveVersion++;
        _resolveCts?.Cancel();
        _resolveCts?.Dispose();
        _resolveCts = null;
        _open = false;
        _activeIndex = -1;
    }
}
