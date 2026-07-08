using Clayzor.Lib.Entities;
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

    // ── Parameters ───────────────────────────────────────────────────────────────
    /// <summary>Заголовок грида.</summary>
    [Parameter] public string Title { get; set; } = "Список";

    /// <summary>DOM-идентификатор корневого элемента грида.</summary>
    [Parameter] public string Id { get; set; } = "clay-grid";

    /// <summary>Данные для отображения.</summary>
    [Parameter] public IEnumerable<TEntity> Items { get; set; } = [];

    /// <summary>Признак загрузки — управляет индикатором грида.</summary>
    [Parameter] public bool Loading { get; set; }

    /// <summary>Показывать кнопку «Добавить» в тулбаре.</summary>
    [Parameter] public bool ShowAddButton { get; set; } = true;

    /// <summary>Количество строк на странице по умолчанию.</summary>
    [Parameter] public int PageSize { get; set; } = 50;

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

    /// <summary>
    /// Текст уведомления после успешного сохранения записи через сервисную колонку.
    /// </summary>
    [Parameter] public string EditSuccessMessage { get; set; } = "Запись обновлена";

    /// <summary>Событие изменения параметров запроса (поиск, сортировка, пагинация и т.д.).</summary>
    [Parameter] public EventCallback<ClayDataQuery> OnQueryChanged { get; set; }

    /// <summary>Общее количество записей — используется для пагинации.</summary>
    [Parameter] public int TotalCount { get; set; }

    /// <summary>
    /// Текущий номер страницы. Передаётся со страницы для синхронизации
    /// пагинатора при внешних изменениях (например, авто-переход в <c>ToggleGroup</c>).
    /// </summary>
    [Parameter] public int PageNumber { get; set; } = 1;

    /// <summary>Показывать панель пагинации.</summary>
    [Parameter] public bool ShowPagination { get; set; } = true;

    /// <summary>Разрешить перетаскивание колонок.</summary>
    [Parameter] public bool AllowColumnReorder { get; set; } = true;

    /// <summary>
    /// Тип данных для каждой фильтруемой колонки: ключ — SQL-имя, значение — <see cref="ColumnType"/>.
    /// Передаётся из <see cref="ClayGridPageBase{T}.FilterColumnTypes"/>.
    /// </summary>
    [Parameter] public IReadOnlyDictionary<string, ColumnType> FilterColumnTypes { get; set; }
        = new Dictionary<string, ColumnType>();

    /// <summary>
    /// Необязательный источник вариантов для выпадающего списка значений в диалоге фильтра.
    /// Ключ — SQL-имя колонки, значение — список вариантов (<see cref="ClayFilterOption"/>).
    /// Передаётся со страницы, которая может переопределить <see cref="ClayGridPageBase{T}.FilterLookupOptions"/>.
    /// </summary>
    [Parameter] public IReadOnlyDictionary<string, IReadOnlyList<ClayFilterOption>>? FilterLookupOptions { get; set; }

    /// <summary>Базовый SQL-запрос SELECT (без WHERE / ORDER BY).</summary>
    [Parameter] public string SelectSql { get; set; } = string.Empty;

    /// <summary>Выходные имена колонок SELECT для полнотекстового поиска.</summary>
    [Parameter] public string[] SearchColumns { get; set; } = [];

    /// <summary>Порядок сортировки по умолчанию.</summary>
    [Parameter] public string DefaultOrder { get; set; } = string.Empty;

    /// <summary>
    /// Тип компонента диалога редактирования.
    /// Диалог должен принимать параметр <c>Model</c> типа сущности.
    /// </summary>
    [Parameter] public Type? EditDialogType { get; set; }

    /// <summary>
    /// Загрузчик данных страницы. Передаётся как <c>DataLoader="this"</c> со страницы,
    /// наследующей <see cref="ClayGridPageBase{T}"/>.
    /// Если задан, <see cref="NotifyQueryChanged"/> вызывает его вместо <see cref="OnQueryChanged"/>.
    /// </summary>
    [Parameter] public IClayGridDataLoader? DataLoader { get; set; }

    /// <summary>
    /// Режим отображения кнопки меню (⋮) в заголовках колонок.
    /// <c>Hidden</c> — всегда скрыта, <c>Always</c> — всегда видна,
    /// <c>Mobile</c> — только на мобильных (≤960px, по умолчанию).
    /// </summary>
    [Parameter] public ColumnMenuMode ColumnMenuMode { get; set; } = ColumnMenuMode.Mobile;

    /// <summary>Показывать кнопку выбора записей (чекбоксы).</summary>
    [Parameter] public bool SelectVisible { get; set; }

    /// <summary>Показывать группу «Печать» в меню групповых операций.</summary>
    [Parameter] public bool ShowPrint { get; set; }

    /// <summary>Показывать группу «Выгрузка в Excel» в меню групповых операций.</summary>
    [Parameter] public bool ShowExcel { get; set; }

    /// <summary>
    /// Глобальное включение фильтра по значению (Excel-style) для всех колонок.
    /// При <c>false</c> значки фильтра по значению не отображаются, даже если
    /// на отдельных колонках установлен <c>AllowValueFilter</c>=<c>true</c>.
    /// По умолчанию <c>true</c>. Используется начиная с задачи V7.
    /// </summary>
    [Parameter] public bool EnableValueFilter { get; set; } = true;

    /// <summary>
    /// Кастомные группы операций для меню групповых операций.
    /// Каждая группа рендерится как подменю со своими операциями.
    /// Обработчики (<see cref="BatchOperation.OnExecute"/>) реализуются в приложении.
    /// </summary>
    [Parameter] public IReadOnlyList<BatchOperationGroup>? CustomBatchGroups { get; set; }

    // ── Lifecycle ────────────────────────────────────────────────────────────────
    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        _pageSize = PageSize;
    }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        _pageNumber = PageNumber;
        if (TotalCount > 0 && _pageNumber > _totalPages)
            _pageNumber = _totalPages;
        if (_selectMode)
            _selectAllChecked = ComputeSelectAllState();
    }

    private bool _loadingChildIds;

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _columnsReady = true;
            _dataKey++;
            StateHasChanged();
        }
        else if (_columnsReady && !string.IsNullOrEmpty(Id))
        {
            _dotnetRef ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("clayGridColumnDrag.init", Id, _dotnetRef);

            if (_selectMode && DataLoader is not null && !_loadingChildIds)
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
    /// Обновляет <see cref="_columnOrder"/> через insert (не swap).
    /// </summary>
    /// <param name="srcSql">SQL-имя перетаскиваемой колонки.</param>
    /// <param name="targetSql">SQL-имя целевой колонки (относительно которой вставляем).</param>
    /// <param name="insertBefore"><c>true</c> — вставить перед <paramref name="targetSql"/>, <c>false</c> — после.</param>
    [JSInvokable]
    public void OnColumnDrop(string srcSql, string targetSql, bool insertBefore)
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
        InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrEmpty(Id))
        {
            try { await JS.InvokeVoidAsync("clayGridColumnDrag.dispose", Id); } catch { }
        }
        _dotnetRef?.Dispose();
    }

    private bool _menuVisible(ClayColumnMeta meta) =>
        meta is not null
        && ColumnMenuMode != ColumnMenuMode.Hidden
        && ((_trayExpanded && meta.Groupable)
            || (_filterTrayExpanded && meta.Filterable));

    /// <summary>
    /// Открывает диалог редактирования для строки детализации.
    /// Вызывается из сервисной колонки с иконкой карандаша.
    /// После успешного сохранения показывает уведомление и перезагружает данные.
    /// </summary>
    private async Task HandleEditClick(IDetailRow detail)
    {
        if (EditDialogType is null) return;
        var parameters = new DialogParameters { ["Model"] = detail.Item };
        var options = new DialogOptionsEx { MaxWidth = MaxWidth.Small, FullWidth = true, DragMode = MudDialogDragMode.Simple };
        var dialog = await DialogService.ShowExAsync(EditDialogType, string.Empty, parameters, options);
        if (!(await dialog.Result)?.Canceled ?? false)
        {
            Snackbar.Add(EditSuccessMessage, Severity.Success);
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
        var items = _columnBySqlName.Values
            .OrderBy(m => _columnOrder.IndexOf(m.ColumnId))
            .Select(m => new ColumnSettingsItem
            {
                SqlName           = m.SqlName,
                DisplayName       = m.DisplayName,
                IsVisible         = !_hiddenSqlNames.Contains(m.SqlName) && !IsGrouped(m.SqlName),
                IsReadonly        = IsGrouped(m.SqlName),
                AllowValueFilter  = !_valueFilterDisabledColumns.Contains(m.SqlName) && m.AllowValueFilter,
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

        return items;
    }

    private async Task OpenColumnSettings()
    {
        _columnOrderSnapshot = [.._columnOrder];

        var items = BuildColumnSettingsItems();

        var parameters = new DialogParameters<ClayColumnSettingsDialog> { { x => x.Items, items } };
        var options = new DialogOptionsEx
        {
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true,
            DragMode = MudDialogDragMode.Simple,
        };
        var dialog = await DialogService.ShowExAsync<ClayColumnSettingsDialog>("Настройка колонок", parameters, options);
        var result = await dialog.Result;
        if (result is not null && !result.Canceled && result.Data is List<ColumnSettingsItem> updatedItems)
        {
            _hiddenSqlNames.Clear();
            _columnOrder.Clear();
            foreach (var item in updatedItems)
            {
                if (!item.IsVisible)
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
                // Сохраняем в disabled-набор только если разработчик разрешил (meta.AllowValueFilter),
                // а пользователь выключил (item.AllowValueFilter == false)
                if (!item.AllowValueFilter && meta.AllowValueFilter)
                    _valueFilterDisabledColumns.Add(item.SqlName);
                // Если нет активного ValueFilter для отключённой колонки — удаляем его
                if (!item.AllowValueFilter)
                    await RemoveValueFilter(item.SqlName);
            }

            _sortState.Clear();
            foreach (var item in updatedItems.Where(i => i.SortPriority > 0).OrderBy(i => i.SortPriority))
            {
                var sortName = _columnBySqlName.TryGetValue(item.SqlName, out var meta3) ? meta3.SortName : item.SqlName;
                _sortState.Add(new SortColumn(sortName, item.IsSortDesc));
            }

            _pageNumber = 1;
            await NotifyQueryChanged();
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

    string IClayGrid.SelectSql        => SelectSql;
    string[] IClayGrid.SearchColumns  => SearchColumns;
    string IClayGrid.DefaultOrder     => DefaultOrder;
    Type? IClayGrid.EditDialogType    => EditDialogType;

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
        var colType = FilterColumnTypes.TryGetValue(sqlName, out var t) ? t : ColumnType.Text;
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

    ColumnMenuMode IClayGrid.ColumnMenuMode       => ColumnMenuMode;
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
