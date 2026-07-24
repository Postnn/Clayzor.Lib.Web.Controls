using Clayzor.Lib.Entities;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Clayzor.Lib.Web.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    private ClayDataQuery _lastQuery = new();
    private int _dataKey;
    private const string ServiceEditColumnKey = "__clay_edit";
    private const string SelectColumnKey = "__clay_select";

    /// <summary>
    /// Срабатывает при регистрации или отмене регистрации колонки.
    /// <see cref="ClayColumn{TEntity}"/> подписывается и вызывает <c>StateHasChanged</c>,
    /// чтобы отобразить <c>DisplayName</c> после того, как <see cref="ClayColumnDef"/>
    /// завершит инициализацию и зарегистрирует метаданные.
    /// </summary>
    public event Action? ColumnsChanged;

    /// <summary>
    /// Срабатывает при открытии или закрытии панели группировки или фильтрации.
    /// <see cref="ClayColumn{TEntity}"/> подписывается и вызывает <c>StateHasChanged</c>,
    /// чтобы показать или скрыть кнопку меню (⋮) в заголовке колонки.
    /// </summary>
    public event Action? TrayStateChanged;

    /// <summary>
    /// Индекс по числовому идентификатору: ColumnId → <see cref="ClayColumnMeta"/>.
    /// Используется <see cref="ClayColumn{TEntity}"/> для поиска метаданных при построении заголовка.
    /// </summary>
    private readonly Dictionary<int, ClayColumnMeta> _columnById = new();

    /// <summary>
    /// Индекс по SQL-имени: SqlName → <see cref="ClayColumnMeta"/>.
    /// Используется для группировки, фильтрации, drag-and-drop и <see cref="IsGrouped"/>.
    /// </summary>
    private readonly Dictionary<string, ClayColumnMeta> _columnBySqlName = new();

    /// <summary>Порядок колонок в гриде (список ColumnId).</summary>
    private readonly List<int> _columnOrder = [];

    /// <summary>SQL-имена колонок, скрытых пользователем через диалог настройки.</summary>
    private readonly HashSet<string> _hiddenSqlNames = [];

    /// <summary>Флаг завершения первой фазы рендеринга (сбор CellTemplate).</summary>
    private bool _columnsReady;

    /// <summary>DotNetObjectReference для передачи в JS (insert-drag заголовков).</summary>
    private DotNetObjectReference<ClayGrid<TEntity>>? _dotnetRef;

    /// <summary>ColumnId → CellTemplate для динамического рендеринга колонок.</summary>
    private readonly Dictionary<int, object> _cellTemplates = [];

    /// <summary>
    /// Действующие настройки грида — ЕДИНСТВЕННЫЙ источник конфигурации внутри компонента.
    /// Собирается в <see cref="ResolveOptions"/> из параметра Options.
    /// Читать только его.
    /// </summary>
    private ClayGridOptions _opt = new();

    private string _gridHeight
    {
        get
        {
            var trays = (_trayExpanded ? 1 : 0) + (_filterTrayExpanded ? 1 : 0);
            return trays switch
            {
                2 => "calc(100vh - 380px)",
                1 => "calc(100vh - 330px)",
                _ => "calc(100vh - 280px)",
            };
        }
    }

    /// <summary>
    /// Настройки приложения Clayzor (внедряются из DI).
    /// Используется для получения URL справки (<see cref="ClayAppSettings.UriHelpClayGrid"/>).
    /// </summary>
    [Inject] public ClayAppSettings ClaySettings { get; set; } = default!;

    /// <summary>Видимость поля поиска: в статике всегда, в динамике — при непустом наборе.</summary>
    private bool _searchVisible => !_opt.Dynamic || _quickSearchEffective.Count > 0;

    // ── Parameters ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Настройки грида. Если задан — устаревшие параметры тега
    /// (<see cref="SelectSql"/>, <see cref="Title"/> и остальные конфигурационные) игнорируются
    /// и не должны задаваться одновременно.
    /// </summary>
    /// <summary>
    /// Настройки грида. Если не задан — используются значения по умолчанию
    /// (<see cref="ClayGridOptions.Defaults"/>).
    /// </summary>
    [Parameter] public ClayGridOptions? Options { get; set; }

    /// <summary>Данные для отображения.</summary>
    [Parameter] public IEnumerable<TEntity> Items { get; set; } = [];

    /// <summary>Признак загрузки — управляет индикатором грида.</summary>
    [Parameter] public bool Loading { get; set; }

    /// <summary>
    /// Колонки грида — <c>ClayColumn</c> / <c>TemplateColumn</c> / <c>PropertyColumn</c>.
    /// Передаются внутрь <c>MudDataGrid.Columns</c>.
    /// </summary>
    [Parameter] public RenderFragment? Columns { get; set; }

    /// <summary>
    /// Метаданные колонок — <see cref="ClayColumnDef"/> компоненты.
    /// Рендерятся вне грида через <c>CascadingValue</c> для регистрации метаданных.
    /// </summary>
    [Parameter] public RenderFragment? ColumnDefs { get; set; }

    /// <summary>Событие нажатия кнопки «Добавить».</summary>
    [Parameter] public EventCallback OnAdd { get; set; }

    /// <summary>Событие изменения параметров запроса (поиск, сортировка, пагинация и т.д.).</summary>
    [Parameter] public EventCallback<ClayDataQuery> OnQueryChanged { get; set; }

    /// <summary>Общее количество записей — используется для пагинации.</summary>
    [Parameter] public int TotalCount { get; set; }

    /// <summary>
    /// Текущий номер страницы. Передаётся со страницы для синхронизации
    /// пагинатора при внешних изменениях (например, авто-переход в <c>ToggleGroup</c>).
    /// </summary>
    [Parameter] public int PageNumber { get; set; } = 1;

    /// <summary>
    /// Загрузчик данных страницы. Передаётся как <c>DataLoader="this"</c> со страницы,
    /// наследующей <see cref="ClayGridPageBase{T}"/>.
    /// Если задан, <see cref="NotifyQueryChanged"/> вызывает его вместо <see cref="OnQueryChanged"/>.
    /// </summary>
    [Parameter] public IClayGridDataLoader? DataLoader { get; set; }

    /// <summary>Есть ли что показать в меню групповых операций.</summary>
    private bool HasBatchOperations => _opt.ShowPrint || _opt.ShowExcel || (_opt.CustomBatchGroups?.Count > 0);

    /// <summary>
    /// Показывать кнопку «Выбрать записи». В динамическом режиме — только если известна
    /// колонка первичного ключа (<c>Запросы.ID</c>): без неё TryGetSelectionId всегда даёт
    /// false, чекбоксов у строк не будет и режим выбора окажется пустышкой.
    /// </summary>
    private bool SelectAvailable
        => _opt.SelectVisible && (!_opt.Dynamic || !string.IsNullOrWhiteSpace(_dynamicDef?.IdColumn));

    // ── Lifecycle ────────────────────────────────────────────────────────────────
    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        _opt = ResolveOptions();
        _pageSize = _opt.PageSize;
    }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        _opt = ResolveOptions();
        // В динамическом режиме пагинация внутренняя: PageNumber снаружи не передают,
        // безусловный сброс убивал переход по страницам.
        if (!_opt.Dynamic)
            _pageNumber = PageNumber;
        if (TotalCount > 0 && _pageNumber > _totalPages)
            _pageNumber = _totalPages;
        if (_selectMode)
            _selectAllChecked = ComputeSelectAllState();
    }

    private bool _loadingChildIds;

    // ── Options resolution ──────────────────────────────────────────────────────

    /// <summary>
    /// Собирает действующие настройки: Options, если задан, иначе — из устаревших параметров тега.
    /// При одновременно заданном Options и отличающемся от значения по умолчанию устаревшем
    /// параметре бросает исключение: молчаливый приоритет одного источника над другим приводит
    /// к настройкам, которых нет в разметке.
    /// </summary>
    private ClayGridOptions ResolveOptions() => Options ?? new ClayGridOptions();

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            if (_opt.Dynamic)
                await InitClientOffset();

            _columnsReady = true;
            _dataKey++;
            StateHasChanged();
        }
        else if (_columnsReady && !string.IsNullOrEmpty(_opt.Id))
        {
            _dotnetRef ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("clayGridColumnDrag.init", _opt.Id, _dotnetRef);

            if (_selectMode && (_opt.Dynamic || DataLoader is not null) && !_loadingChildIds)
            {
                var missingKeys = new List<string>();
                foreach (var row in Items ?? [])
                {
                    if (row is GroupHeaderRow gh && !_groupChildIds.ContainsKey(gh.FullKey))
                        missingKeys.Add(gh.FullKey);
                }
                if (missingKeys.Count > 0)
                {
                    _loadingChildIds = true;
                    try
                    {
                        await LoadChildIdsForGroupsAsync(missingKeys);
                        StateHasChanged();
                    }
                    finally
                    {
                        _loadingChildIds = false;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Вызывается из JS при начале drag заголовка колонки.
    /// Устанавливает <see cref="ClayDragState.DraggedColumn"/> для tray-drop обработчиков.
    /// </summary>
    [JSInvokable]
    public void SetDraggedColumn(string? sqlName)
        => ClayDragState.DraggedColumn = sqlName;

    /// <summary>
    /// Вызывается из JS после завершения drag-and-drop заголовка колонки.
    /// Обновляет <see cref="_columnOrder"/> через insert (не swap) и в динамическом режиме
    /// сохраняет новый порядок (данные не перезагружаются — двигаются только столбцы).
    /// </summary>
    /// <param name="srcSql">SQL-имя перетаскиваемой колонки.</param>
    /// <param name="targetSql">SQL-имя целевой колонки (относительно которой вставляем).</param>
    /// <param name="insertBefore"><c>true</c> — вставить перед <paramref name="targetSql"/>, <c>false</c> — после.</param>
    [JSInvokable]
    public async Task OnColumnDrop(string srcSql, string targetSql, bool insertBefore)
    {
        if (!_columnBySqlName.TryGetValue(srcSql, out var srcMeta)) return;
        if (!_columnBySqlName.TryGetValue(targetSql, out var tgtMeta)) return;

        var srcId = srcMeta.ColumnId;
        var tgtId = tgtMeta.ColumnId;

        var srcIdx = _columnOrder.IndexOf(srcId);
        var tgtIdx = _columnOrder.IndexOf(tgtId);
        if (srcIdx < 0 || tgtIdx < 0 || srcIdx == tgtIdx) return;

        _columnOrder.RemoveAt(srcIdx);
        tgtIdx = _columnOrder.IndexOf(tgtId);
        var insertAt = insertBefore ? tgtIdx : tgtIdx + 1;
        insertAt = Math.Clamp(insertAt, 0, _columnOrder.Count);
        _columnOrder.Insert(insertAt, srcId);

        _dataKey++;
        StateHasChanged();

        // Порядок колонок — часть сохраняемого состояния. Диалог настройки колонок
        // сохраняет его через NotifyQueryChanged → LoadDynamicData → SaveDynamicState;
        // перетаскивание данные не меняет, поэтому сохраняем состояние напрямую, без
        // перезагрузки строк. SaveDynamicState идемпотентен и пишет только изменившийся
        // параметр (GF12).
        if (_opt.Dynamic)
            await SaveDynamicState();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_opt.Id))
        {
            try { await JS.InvokeVoidAsync("clayGridColumnDrag.dispose", _opt.Id); } catch { }
        }
        _dotnetRef?.Dispose();
    }

    private bool _menuVisible(ClayColumnMeta meta) =>
        meta is not null
        && _opt.ColumnMenuMode != ColumnMenuMode.Hidden
        && ((_trayExpanded && meta.Groupable)
            || (_filterTrayExpanded && meta.Filterable));

    /// <summary>
    /// Открывает диалог редактирования для строки детализации.
    /// Вызывается из сервисной колонки с иконкой карандаша.
    /// После успешного сохранения показывает уведомление и перезагружает данные.
    /// </summary>
    private async Task HandleEditClick(IDetailRow detail)
    {
        if (_opt.EditDialogType is null) return;
        var parameters = new DialogParameters { ["Model"] = detail.Item };
        var options = new DialogOptionsEx { MaxWidth = MaxWidth.Small, FullWidth = true, DragMode = MudDialogDragMode.Simple };
        var dialog = await DialogService.ShowExAsync(_opt.EditDialogType, string.Empty, parameters, options);
        if (!(await dialog.Result)?.Canceled ?? false)
        {
            Snackbar.Add(_opt.EditSuccessMessage, Severity.Success);
            await NotifyQueryChanged();
        }
    }

    private List<int> _columnOrderSnapshot = [];

    /// <summary>
    /// Строит список <see cref="ColumnSettingsItem"/> из текущего состояния грида:
    /// порядок колонок, видимость, признак группировки и состояние сортировки.
    /// Переиспользуется в <see cref="OpenColumnSettings"/> и при подготовке колонок
    /// к печати/экспорту.
    /// </summary>
    private List<ColumnSettingsItem> BuildColumnSettingsItems()
    {
        // Строим из _columnOrder, а не из _columnBySqlName — фильтр-онли колонки
        // (Тип 6/11) есть в _columnBySqlName, но не в _columnOrder, и не должны
        // попадать в диалог настройки колонок.
        var items = _columnOrder
            .Select(id => _columnById.GetValueOrDefault(id))
            .Where(m => m is not null)
            .Select(m => new ColumnSettingsItem
            {
                SqlName           = m!.SqlName,
                DisplayName       = m.DisplayName,
                IsVisible         = !_hiddenSqlNames.Contains(m.SqlName) && !IsGrouped(m.SqlName),
                IsReadonly        = IsGrouped(m.SqlName),
                Groupable         = m.Groupable,
                AllowValueFilter  = !_valueFilterDisabledColumns.Contains(m.SqlName) && m.AllowValueFilter,
                QuickSearch       = _quickSearchEffective.Contains(m.SqlName),
                QuickSearchDisabled = !ClayColumnKindExtensions.SupportsQuickSearch(
                    _dynamicCols.FirstOrDefault(c => c.Column == m.SqlName)?.Type ?? 0),
                QuickSearchDefault = _dynamicCols.FirstOrDefault(c => c.Column == m.SqlName)?.QuickSearch ?? false,
            })
            .ToList();

        for (int i = 0; i < _sortState.Count; i++)
        {
            var sc = _sortState[i];
            var match = items.FirstOrDefault(it =>
                _columnBySqlName.TryGetValue(it.SqlName, out var m) && m.SortName == sc.Column);
            if (match is not null)
            {
                match.SortPriority = i + 1;
                match.IsSortDesc   = sc.Desc;
            }
        }

        for (int i = 0; i < _groupColumns.Count; i++)
        {
            var match = items.FirstOrDefault(it => it.SqlName == _groupColumns[i]);
            if (match is not null)
                match.GroupPriority = i + 1;
        }

        return items;
    }

    private async Task OpenColumnSettings()
    {
        _columnOrderSnapshot = [.._columnOrder];

        var items = BuildColumnSettingsItems();

        var parameters = new DialogParameters<ClayColumnSettingsDialog>
        {
            { x => x.Items,        items },
            { x => x.ShowGrouping, _trayExpanded && _columnById.Values.Any(m => m.Groupable) },
            { x => x.ShowQuickSearch, _opt.Dynamic && _dynamicDef?.SupportsQuickSearch == true },
        };
        var options = new DialogOptionsEx
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
            DragMode = MudDialogDragMode.Simple,
        };
        var dialog = await DialogService.ShowExAsync<ClayColumnSettingsDialog>("Настройка колонок", parameters, options);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled && result.Data is List<ColumnSettingsItem> updatedItems)
        {
            // Снапшоты для определения, изменилось ли что-то кроме быстрого поиска
            var snapSort         = _sortState.Select(s => (s.Column, s.Desc)).ToList();
            var snapGroups       = _groupColumns.ToList();
            var snapValueFilter  = _valueFilterDisabledColumns.ToHashSet();

            // Невидимость сгруппированной колонки — следствие группировки, а не выбора
            // пользователя. Записывать её в _hiddenSqlNames нельзя: снимут группировку —
            // колонка обязана вернуться. Для таких колонок сохраняем прежний признак.
            var hiddenBefore = new HashSet<string>(_hiddenSqlNames);

            _hiddenSqlNames.Clear();
            _columnOrder.Clear();
            foreach (var item in updatedItems)
            {
                var hidden = item.GroupPriority > 0
                    ? hiddenBefore.Contains(item.SqlName)
                    : !item.IsVisible;
                if (hidden)
                    _hiddenSqlNames.Add(item.SqlName);
                if (_columnBySqlName.TryGetValue(item.SqlName, out var meta2))
                    _columnOrder.Add(meta2.ColumnId);
            }

            // AllowValueFilter: сохраняем пользовательские переопределения
            _valueFilterDisabledColumns.Clear();
            foreach (var item in updatedItems)
            {
                var meta = _columnBySqlName.GetValueOrDefault(item.SqlName);
                if (meta is null) continue;
                if (!item.AllowValueFilter && meta.AllowValueFilter)
                    _valueFilterDisabledColumns.Add(item.SqlName);
                if (!item.AllowValueFilter)
                    await RemoveValueFilter(item.SqlName);
            }

            _sortState.Clear();
            foreach (var item in updatedItems.Where(i => i.SortPriority > 0).OrderBy(i => i.SortPriority))
            {
                var sortName = _columnBySqlName.TryGetValue(item.SqlName, out var meta3) ? meta3.SortName : item.SqlName;
                _sortState.Add(new SortColumn(sortName, item.IsSortDesc));
            }

            _groupColumns.Clear();
            foreach (var item in updatedItems.Where(i => i.GroupPriority > 0).OrderBy(i => i.GroupPriority))
                _groupColumns.Add(item.SqlName);

            // Быстрый поиск: обновить набор и пересчитать SearchColumns
            _dynamicQuickSearchCols.Clear();
            foreach (var item in updatedItems.Where(i => i.QuickSearch && !i.QuickSearchDisabled))
                _dynamicQuickSearchCols.Add(item.SqlName);
            var quickSearchReloaded = false;
            if (_opt.Dynamic && _dynamicDef?.SupportsQuickSearch == true)
            {
                await SaveDynamicState();
                quickSearchReloaded = await RefreshQuickSearchEffective(DynamicOpts.Value);
                StateHasChanged();
            }

            if (_opt.Dynamic) ResetDynamicExpandedGroups();   // GG7: ключи старой группировки протухли

            // Проверить, изменилось ли что-то кроме быстрого поиска
            var dataChanged =
                !_columnOrder.SequenceEqual(_columnOrderSnapshot) ||
                !_hiddenSqlNames.SetEquals(hiddenBefore) ||
                _sortState.Count != snapSort.Count ||
                !_sortState.Select(s => (s.Column, s.Desc)).SequenceEqual(snapSort) ||
                !_groupColumns.SequenceEqual(snapGroups) ||
                !_valueFilterDisabledColumns.SetEquals(snapValueFilter);

            if (!quickSearchReloaded && dataChanged)
            {
                _pageNumber = 1;
                await NotifyQueryChanged();
            }
        }
        else
        {
            _columnOrder.Clear();
            _columnOrder.AddRange(_columnOrderSnapshot);
            _dataKey++;
            StateHasChanged();
        }
    }

    // ── Core notification ────────────────────────────────────────────────────────
    private async Task NotifyQueryChanged()
    {
        _dataKey++;
        ClearGroupChildCache();
        var query = new ClayDataQuery
        {
            SearchText      = _searchText,
            GroupEnabled    = _groupColumns.Count > 0,
            GroupColumns    = _groupColumns.ToList(),
            SortColumns     = _sortState.ToList(),
            PageNumber      = _pageNumber,
            PageSize        = _pageSize,
            CompositeFilter = _filterRoot,
        };

        if (_selectMode && _lastQuery.PageNumber != 0)
        {
            var prevLeafCount = _lastQuery.CompositeFilter?.Nodes.Count ?? 0;
            var curLeafCount  = query.CompositeFilter?.Nodes.Count ?? 0;
            var essenceChanged =
                _lastQuery.SearchText != query.SearchText ||
                !_lastQuery.GroupColumns.SequenceEqual(query.GroupColumns) ||
                !_lastQuery.SortColumns.SequenceEqual(query.SortColumns) ||
                prevLeafCount != curLeafCount;
            if (essenceChanged)
            {
                _selectedIds.Clear();
                _selectAllChecked = false;
            }
        }

        _lastQuery = query;

        if (_opt.Dynamic)
        {
            await LoadDynamicData(query);
            StateHasChanged();
            return;
        }

        if (DataLoader is not null)
            await DataLoader.OnQueryChangedAsync(query);
        else
            await OnQueryChanged.InvokeAsync(query);
    }

    // ── IClayGrid — реализация интерфейса ───────────────────────────────────────

    event Action? IClayGrid.ColumnsChanged
    {
        add    => ColumnsChanged += value;
        remove => ColumnsChanged -= value;
    }

    event Action? IClayGrid.TrayStateChanged
    {
        add    => TrayStateChanged += value;
        remove => TrayStateChanged -= value;
    }

    ClayGridOptions IClayGrid.Options => _opt;

    bool IClayGrid.IsGrouped(string sqlName) => IsGrouped(sqlName);

    Task IClayGrid.ToggleSort(string sqlName)             => ToggleSort(sqlName);
    RenderFragment IClayGrid.GetSortBadge(string sqlName) => GetSortBadge(sqlName);

    /// <summary>
    /// Возвращает метаданные колонки по её SQL-имени, либо <c>null</c>.
    /// </summary>
    public ClayColumnMeta? GetColumnMeta(string sqlName)
        => _columnBySqlName.TryGetValue(sqlName, out var m) ? m : null;

    ClayColumnMeta? IClayGrid.GetColumnMeta(string sqlName) => GetColumnMeta(sqlName);

    /// <summary>
    /// Возвращает метаданные колонки по числовому <c>ColumnId</c>, либо <c>null</c>.
    /// </summary>
    public ClayColumnMeta? GetColumnMetaById(int columnId)
        => _columnById.TryGetValue(columnId, out var m) ? m : null;

    ClayColumnMeta? IClayGrid.GetColumnMetaById(int columnId) => GetColumnMetaById(columnId);

    /// <summary>
    /// Регистрирует колонку. Вызывается из <see cref="ClayColumnDef"/> при инициализации.
    /// Поддерживает два индекса: по <paramref name="columnId"/> и по <paramref name="sqlName"/>.
    /// </summary>
    void IClayGrid.RegisterColumn(int columnId, string sqlName, string displayName, bool groupable, bool filterable, string? sortName, bool allowValueFilter, string? boolTrueLabel, string? boolFalseLabel)
    {
        if (string.IsNullOrEmpty(sqlName)) return;
        var colType = _opt.FilterColumnTypes.TryGetValue(sqlName, out var t) ? t : ColumnType.Text;
        var meta = new ClayColumnMeta
        {
            ColumnId         = columnId,
            SqlName          = sqlName,
            DisplayName      = displayName,
            SortName         = string.IsNullOrEmpty(sortName) ? sqlName : sortName,
            Groupable        = groupable,
            Filterable       = filterable,
            AllowValueFilter = allowValueFilter,
            BoolTrueLabel    = boolTrueLabel,
            BoolFalseLabel   = boolFalseLabel,
            Type             = ColumnTypes.ColumnTypeRegistry.FromKind(colType),
        };
        _columnById[columnId]     = meta;
        _columnBySqlName[sqlName] = meta;
        ColumnsChanged?.Invoke();
    }

    /// <summary>
    /// Отменяет регистрацию колонки при уничтожении <see cref="ClayColumnDef"/>.
    /// </summary>
    void IClayGrid.UnregisterColumn(int columnId, string sqlName)
    {
        _columnById.Remove(columnId);
        _columnBySqlName.Remove(sqlName);
        ColumnsChanged?.Invoke();
    }

    bool IClayGrid.IsGroupingTrayExpanded         => _trayExpanded;
    bool IClayGrid.IsFilterTrayExpanded           => _filterTrayExpanded;

    Task IClayGrid.AddGroupAsync(string sqlName)  => AddGroupColumn(sqlName);

    Task IClayGrid.AddFilterAsync(string sqlName)
        => OpenFilterDialog(sqlName, _columnBySqlName.TryGetValue(sqlName, out var m) ? m.DisplayName : sqlName);

    void IClayGrid.RegisterColumnInOrder(int columnId)
    {
        if (!_columnOrder.Contains(columnId))
            _columnOrder.Add(columnId);
    }

    void IClayGrid.RegisterCellTemplate(int columnId, object template)
        => _cellTemplates[columnId] = template;

    bool IClayGrid.IsColumnHidden(string sqlName) => _hiddenSqlNames.Contains(sqlName);

    Filter.ClayFilterGroupNode? IClayGrid.ActiveCompositeFilter => ActiveCompositeFilter;

    Task IClayGrid.OpenCompositeFilterDialog() => OpenCompositeFilterDialog();

    IReadOnlyList<ClayColumnMeta> IClayGrid.GetVisibleColumns()
        => _columnOrder
            .Select(id => _columnById.GetValueOrDefault(id))
            .Where(meta => meta is not null
                && !_hiddenSqlNames.Contains(meta.SqlName)
                && !IsGrouped(meta.SqlName))
            .ToList()!;
}
