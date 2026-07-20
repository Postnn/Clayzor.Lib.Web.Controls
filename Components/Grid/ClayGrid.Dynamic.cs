using Clayzor.Lib.DALC;
using Clayzor.Lib.Entities;
using Clayzor.Lib.Entities.DynamicGrid;
using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Clayzor.Lib.Web.Controls.Components.Grid.Filter;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;
using Dapper;
using System.Web;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Динамический режим ClayGrid: грид загружает определение (SQL, колонки, кнопки)
/// и пользовательские параметры из БД вместо статической разметки.
/// </summary>
public partial class ClayGrid<TEntity> where TEntity : class
{
    [Inject] private DbManager Db { get; set; } = default!;
    [Inject] private IOptions<ClayGridDynamicOptions> DynamicOpts { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    /// <summary>Включает динамический режим (чтение определения из БД).</summary>
    [Parameter] public bool Dynamic { get; set; }

    /// <summary>
    /// Код запроса (GridId). Если не задан — берётся из query-параметра
    /// с именем <see cref="ClayGridDynamicOptions.GridIdQueryParam"/>.
    /// </summary>
    [Parameter] public int? DynamicGridId { get; set; }

    [Inject] private IConfiguration Config { get; set; } = default!;

    private ClayGridDefinition? _dynamicDef;
    private IReadOnlyList<ClayColumnDefinition> _dynamicCols = [];
    private bool _dynamicInitDone;
    private HashSet<string> _dynamicKnownColumns = [];
    private Dictionary<string, IReadOnlyDictionary<string, string>> _dynamicLookups = [];
    private Dictionary<string, IReadOnlyDictionary<string, (string Tooltip, string Href)>> _dynamicIconLookups = [];

    // Закешированные URL/SQL действий
    private string? _dynamicEditUrl;
    private string? _dynamicNewUrl;
    private string? _dynamicDeleteSql;

    // Смещение часового пояса клиента (для Тип 10/13)
    private TimeSpan _clientOffset = TimeSpan.Zero;

    // Ошибка инициализации динамического режима
    private string? _dynamicError;

    /// <summary>
    /// Читает смещение часового пояса клиента через JS. Вызывается только из
    /// OnAfterRenderAsync(firstRender): при пререндере JS недоступен.
    /// </summary>
    private async Task InitClientOffset()
    {
        try
        {
            var minutes = await JS.InvokeAsync<int>("clayGridTimeZone.getOffsetMinutes", Array.Empty<object?>());
            var offset  = TimeSpan.FromMinutes(minutes);
            if (offset == _clientOffset) return;

            _clientOffset = offset;
            _dataKey++;              // пересобрать ячейки с уже новым смещением
            StateHasChanged();
        }
        catch
        {
            // JS недоступен (пререндер/отвал) — остаёмся на UTC
        }
    }

    // ID грида и CLID для персистенции состояния
    private int _dynamicGridId;
    private int _dynamicClid;
    /// <summary>Кеш «что сейчас лежит в БД» — ключ: имя параметра, значение: сохранённая строка.</summary>
    private Dictionary<string, string> _dynamicSavedParams = [];
    private HashSet<string> _dynamicForcedParamNames = [];

    /// <summary>
    /// Инициализация динамического режима при первом рендере.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        if (Dynamic && !_dynamicInitDone)
            await InitDynamicMode();
    }

    private async Task InitDynamicMode()
    {
        var opt = DynamicOpts.Value;
        var gridId = ResolveDynamicGridId(opt);

        if (gridId == 0)
        {
            _dynamicError = $"Не указан код запроса: ожидается query-параметр «{opt.GridIdQueryParam}».";
            return;
        }

        _dynamicGridId = gridId;
        _dynamicClid   = ResolveClientId(opt);

        _dynamicDef = await ClayGridDefinitionData.LoadGridAsync(Db, gridId, opt.SettingsTable, opt.Schema);
        if (_dynamicDef is null)
        {
            _dynamicError = $"Грид не найден: запрос №{gridId} отсутствует в «{opt.SettingsTable}».";
            return;
        }

        Title     = _dynamicDef.Title ?? "Список";
        SelectSql = _dynamicDef.Sql;

        _dynamicCols = await ClayGridDefinitionData.LoadColumnsAsync(Db, gridId, opt.ColumnsTable, opt.Schema);

        // Колонки вывода: сначала видимые по Порядок, затем скрытые (Порядок 0/NULL).
        // Фильтр-онли типы (6, 11) в вывод не идут — они регистрируются отдельно.
        var gridCols = _dynamicCols
            .Where(c => c.Type != (int)ClayColumnKind.ConditionBool
                     && c.Type != (int)ClayColumnKind.ConditionList)
            .OrderBy(c => c.Order is > 0 ? 0 : 1)
            .ThenBy(c => c.Order ?? int.MaxValue)
            .ThenBy(c => c.Order is > 0 ? c.ColumnId.ToString("D10") : c.Column)
            .ToList();

        var visibleCols = gridCols.Where(c => c.Order is > 0).ToList();

        SearchColumns = visibleCols.Select(c => c.Column).ToArray();
        DefaultOrder  = string.Join(", ", visibleCols.Select(c => c.Column));
        _dynamicKnownColumns = gridCols.Select(c => c.Column).ToHashSet();

        // Загружаем справочники для колонок типа 5 (Список)
        foreach (var col in gridCols.Where(c => c.Type == (int)ClayColumnKind.List))
        {
            if (!string.IsNullOrWhiteSpace(col.Format))
            {
                try
                {
                    var pairs = await DynamicSql.QueryPairsAsync(Db, col.Format);
                    _dynamicLookups[col.Column] = pairs
                        .Where(p => p.Value is not null)
                        .ToDictionary(p => p.Value?.ToString() ?? "", p => p.Text ?? "");
                }
                catch { /* справочник не загрузился — покажем value как есть */ }
            }
        }

        // Загружаем справочники для колонок типа 9 (Пиктограмма)
        foreach (var col in gridCols.Where(c => c.Type == (int)ClayColumnKind.Icon))
        {
            if (!string.IsNullOrWhiteSpace(col.Format))
            {
                try
                {
                    var triples = await DynamicSql.QueryTriplesAsync(Db, col.Format);
                    _dynamicIconLookups[col.Column] = triples
                        .Where(t => t.Value is not null)
                        .ToDictionary(t => t.Value?.ToString() ?? "", t => (t.Text ?? "", t.Icon ?? ""));
                }
                catch { /* справочник иконок не загрузился */ }
            }
        }

        // Регистрируем фильтр-онли колонки (Тип 6, 11): не выводятся в гриде, только фильтрация
        foreach (var col in _dynamicCols.Where(c => c.Type == (int)ClayColumnKind.ConditionBool || c.Type == (int)ClayColumnKind.ConditionList))
        {
            var desc = ClayColumnTypeMap.Resolve(col.Type);
            if (desc is null) continue;

            var meta = new ClayColumnMeta
            {
                ColumnId    = col.ColumnId,
                SqlName     = col.Column,
                DisplayName = col.Header ?? col.Column,
                SortName    = col.Column,
                Groupable   = false,
                Filterable  = true,
                Type        = desc,
            };
            _columnById[col.ColumnId]     = meta;
            _columnBySqlName[col.Column]  = meta;
            _dynamicKnownColumns.Add(col.Column);
        }

        foreach (var col in gridCols)
        {
            var desc = ClayColumnTypeMap.Resolve(col.Type);
            if (desc is null) continue; // неподдержанный тип — пропускаем с логом

            var meta = new ClayColumnMeta
            {
                ColumnId    = col.ColumnId,
                SqlName     = col.Column,
                DisplayName = col.Header ?? col.Column,
                SortName    = col.Column,
                Groupable   = true,
                Filterable  = col.Type != (int)ClayColumnKind.List,
                Type        = desc,
            };
            _columnById[col.ColumnId]     = meta;
            _columnBySqlName[col.Column]  = meta;
            _columnOrder.Add(col.ColumnId);

            // Порядок 0/NULL — скрыта по умолчанию, но доступна в «Настройке колонок»
            if (col.Order is not > 0)
                _hiddenSqlNames.Add(col.Column);

            // Кешируем имя колонки для замыкания
            var colName    = col.Column;
            var lookup     = _dynamicLookups.GetValueOrDefault(col.Column);
            var iconLookup = _dynamicIconLookups.GetValueOrDefault(col.Column);
            var isList     = col.Type == (int)ClayColumnKind.List;
            var isIcon     = col.Type == (int)ClayColumnKind.Icon;
            var isHtml     = col.Type == (int)ClayColumnKind.Html;
            var isLink     = col.Type == (int)ClayColumnKind.Link;
            var isLimText   = col.Type == (int)ClayColumnKind.LimitedText;
            var isDateTime  = col.Type == (int)ClayColumnKind.DateTimeLocal;
            var isTime      = col.Type == (int)ClayColumnKind.TimeLocal;
            var limLen      = isLimText && int.TryParse(col.Format, out var n) ? n : 0;
            var dtFormat    = isDateTime || isTime ? col.Format : null;
            _cellTemplates[col.ColumnId] = (RenderFragment<CellContext<TEntity>>)(ctx =>
            {
                string text = "";
                string? iconHref = null;
                string? iconTitle = null;
                if (ctx.Item is IReadOnlyDictionary<string, object?> dict
                    && dict.TryGetValue(colName, out var v) && v is not null)
                {
                    var raw = v.ToString()!;
                    if (isIcon && iconLookup is not null && iconLookup.TryGetValue(raw, out var iconData))
                    {
                        iconHref  = iconData.Href;
                        iconTitle = iconData.Tooltip;
                    }
                    else if (isList && lookup is not null && lookup.TryGetValue(raw, out var display))
                    {
                        text = display;
                    }
                    else if (isHtml)
                    {
                        text = ClayHtmlSanitizer.Sanitize(raw);
                    }
                    else if (isDateTime || isTime)
                    {
                        text = ClayDateTimeConverter.Format(v, dtFormat, _clientOffset);
                    }
                    else
                    {
                        text = raw;
                    }
                }
                return (RenderFragment)(builder =>
                {
                    if (isIcon && iconHref is not null)
                    {
                        builder.OpenElement(0, "img");
                        builder.AddAttribute(1, "src", iconHref);
                        if (!string.IsNullOrEmpty(iconTitle))
                            builder.AddAttribute(2, "title", iconTitle);
                        builder.AddAttribute(3, "style", "width:16px;height:16px");
                        builder.CloseElement();
                    }
                    else if (isLink && !string.IsNullOrEmpty(text))
                    {
                        builder.OpenElement(0, "a");
                        builder.AddAttribute(1, "href", ClayHtmlSanitizer.Sanitize(text));
                        builder.AddContent(2, text);
                        builder.CloseElement();
                    }
                    else if (isLimText)
                    {
                        var display = limLen > 0 && text.Length > limLen
                            ? text[..limLen] + "…"
                            : text;
                        if (display != text)
                        {
                            builder.OpenElement(0, "span");
                            builder.AddAttribute(1, "title", text);
                            builder.AddContent(2, display);
                            builder.CloseElement();
                        }
                        else
                        {
                            builder.AddContent(0, display);
                        }
                    }
                    else if (isHtml)
                    {
                        builder.AddMarkupContent(0, text);
                    }
                    else
                    {
                        builder.AddContent(0, text);
                    }
                });
            });
        }

        // Действия строк: резолвим URL/SQL из определения
        _dynamicEditUrl   = ClayGridLinkResolver.Resolve(_dynamicDef.EditForm, Config);
        _dynamicNewUrl    = ClayGridLinkResolver.Resolve(_dynamicDef.NewForm, Config);
        _dynamicDeleteSql = string.IsNullOrWhiteSpace(_dynamicDef.SqlDelete) ? null : _dynamicDef.SqlDelete;

        // Восстановление сохранённого состояния пользователя
        await RestoreDynamicState(opt);

        // Применить URL-параметры (фильтры и колонки)
        ApplyUrlParams(opt);

        _dynamicInitDone = true;

        // Первая загрузка: в динамическом режиме страницы-загрузчика нет,
        // грид обязан стартовать сам.
        await NotifyQueryChanged();
    }

    // ── Динамические действия ─────────────────────────────────────────────────

    /// <summary>Признак, что в динамическом режиме есть колонка редактирования.</summary>
    private bool HasDynamicEdit => _dynamicEditUrl is not null;

    /// <summary>Признак, что в динамическом режиме есть кнопка добавления.</summary>
    private bool HasDynamicAdd => _dynamicNewUrl is not null;

    /// <summary>Признак, что в динамическом режиме есть кнопка удаления.</summary>
    private bool HasDynamicDelete => _dynamicDeleteSql is not null;

    /// <summary>CSS-стиль сервисной колонки (ширина зависит от наличия кнопки удаления).</summary>
    private string GetEditColumnStyle()
    {
        var w = HasDynamicDelete ? "88px" : "44px";
        return $"width:{w};min-width:{w};max-width:{w}";
    }

    /// <summary>Единый обработчик клика по карандашу (статический + динамический).</summary>
    private async Task HandleRowEditClick(IDetailRow detail)
    {
        if (HasDynamicEdit)
            await HandleDynamicEdit(detail);
        else
            await HandleEditClick(detail);
    }

    private async Task HandleDynamicEdit(IDetailRow detail)
    {
        var idVal = GetRowIdValue(detail.Item);
        if (idVal is null) return;
        var url = $"{_dynamicEditUrl}?{_dynamicDef!.IdColumn}={Uri.EscapeDataString(idVal)}";
        Nav.NavigateTo(url);
    }

    /// <summary>Единый обработчик клика по кнопке «+» (статический + динамический).</summary>
    private async Task HandleRowAddClick()
    {
        if (HasDynamicAdd)
            Nav.NavigateTo(_dynamicNewUrl!);
        else
            await OnAdd.InvokeAsync();
    }

    /// <summary>Обработчик клика по кнопке удаления строки.</summary>
    private async Task HandleDynamicDelete(object? rowItem)
    {
        var idVal = GetRowIdValue(rowItem);
        if (idVal is null || _dynamicDeleteSql is null) return;

        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Message, "Удалить запись?" }
        };
        var options = new DialogOptionsEx { DragMode = MudDialogDragMode.Simple };
        var dialog = await DialogService.ShowExAsync<ConfirmDialog>("Подтверждение", parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled) return;

        await DynamicSql.ExecuteAsync(Db, _dynamicDeleteSql, new { id = idVal });
        await NotifyQueryChanged();
    }

    /// <summary>Извлекает значение ID строки из словаря-строки по IdColumn.</summary>
    private string? GetRowIdValue(object? rowItem)
    {
        if (_dynamicDef is null) return null;
        if (rowItem is IReadOnlyDictionary<string, object?> dict
            && dict.TryGetValue(_dynamicDef.IdColumn!, out var v) && v is not null)
            return v.ToString();
        return null;
    }

    /// <summary>
    /// ID строки для режима выбора. В динамическом режиме берётся из колонки
    /// <c>_dynamicDef.IdColumn</c>, в статическом — из <see cref="Entity.Id"/>.
    /// Возвращает false, если ID нечисловой: выбор для такого грида недоступен.
    /// </summary>
    private bool TryGetSelectionId(object? rowItem, out int id)
    {
        id = 0;

        if (Dynamic)
        {
            var raw = GetRowIdValue(rowItem);
            return raw is not null && int.TryParse(raw, out id);
        }

        if (rowItem is Entity e)
        {
            id = e.Id;
            return true;
        }

        return false;
    }

    private int ResolveDynamicGridId(ClayGridDynamicOptions opt)
    {
        if (DynamicGridId.HasValue && DynamicGridId.Value != 0)
            return DynamicGridId.Value;

        var uri  = new Uri(Nav.Uri);
        var qs   = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var val  = qs[opt.GridIdQueryParam];
        if (val is not null && int.TryParse(val, out var gid))
            return gid;

        return 0;
    }

    /// <summary>
    /// Загружает данные в динамическом режиме через <see cref="DynamicSql"/>.
    /// Вызывается из <see cref="ClayGrid{TEntity}.NotifyQueryChanged"/> вместо
    /// делегирования странице через <see cref="IClayGridDataLoader"/>.
    /// </summary>
    private async Task LoadDynamicData(ClayDataQuery query)
    {
        await RunBusyAsync("Загрузка данных…", async () =>
        {
            // NotifyQueryChanged собирает query без ExpandedGroups (в статике их владелец — страница).
            query.ExpandedGroups = _dynamicExpandedGroups;

            var dp = new DynamicParameters();

            // BuildWhereClause генерирует "col LIKE @search", но параметр не добавляет —
            // это делает вызывающий (ср. ClayGridPageBase.LoadFlatData).
            dp.Add("search", $"%{query.SearchText}%");

            var searchWhere = query.BuildWhereClause(SearchColumns);
            var filterWhere = ClayCompositeSqlBuilder.Build(query.CompositeFilter, dp, _dynamicKnownColumns);
            var where       = ClayDataQuery.CombineWhere(searchWhere, filterWhere);

            if (query.GroupEnabled && query.GroupColumns.Count > 0)
                await LoadDynamicGroupedData(query, where, dp);
            else
                await LoadDynamicFlatData(query, where, dp);

            // Сохраняем состояние после каждой загрузки данных
            await SaveDynamicState();
        });
    }

    /// <summary>Плоский режим: страница строк без группировки.</summary>
    private async Task LoadDynamicFlatData(ClayDataQuery query, string? where, DynamicParameters dp)
    {
        _dynamicGroupRoots       = null;
        _dynamicGroupKeysByDepth = null;
        _dynamicGroupWhere       = null;
        _dynamicGroupParams      = null;
        _dynamicGroupExprs       = [];

        var orderBy = query.BuildOrderBy(DefaultOrder);

        var rows = await DynamicSql.QueryPagedRowsAsync(
            Db, SelectSql, where, orderBy, dp, query.PageNumber, query.PageSize);

        TotalCount = await DynamicSql.QueryCountAsync(Db, SelectSql, where, dp);
        Items      = rows.Select(r => (TEntity)(object)new ClayDynamicRow(r)).ToList();
    }

    // ── Персистенция состояния ─────────────────────────────────────────────────

    private int ResolveClientId(ClayGridDynamicOptions opt)
    {
        var uri  = new Uri(Nav.Uri);
        var qs   = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var val  = qs[opt.ClientIdQueryParam];
        return val is not null && int.TryParse(val, out var clid) ? clid : 0;
    }

    private async Task RestoreDynamicState(ClayGridDynamicOptions opt)
    {
        var p = (string prefix) => ClayGridUserParamsData.BuildParamName(prefix, _dynamicGridId);
        var paramNames = new[] {
            p(opt.ColumnsParamPrefix), p(opt.FilterParamPrefix),
            p(opt.GroupingParamPrefix), p(opt.SortingParamPrefix), p(opt.PageSizeParamPrefix)
        };

        var saved = await ClayGridUserParamsData.LoadAsync(
            Db, _dynamicClid, paramNames, opt.UserParamsTable, opt.Schema);

        _dynamicSavedParams = new Dictionary<string, string>(saved);

        // Видимость/порядок колонок
        var colsName = p(opt.ColumnsParamPrefix);
        if (saved.TryGetValue(colsName, out var colsVal))
            ApplyColumnsState(colsVal);

        // Сортировка
        var srtName = p(opt.SortingParamPrefix);
        if (saved.TryGetValue(srtName, out var srtVal))
            ApplySavedSort(srtVal);

        // Группировка
        var grpName = p(opt.GroupingParamPrefix);
        if (saved.TryGetValue(grpName, out var grpVal))
            ApplySavedGroups(grpVal);

        // Размер страницы
        var pgsName = p(opt.PageSizeParamPrefix);
        if (saved.TryGetValue(pgsName, out var pgsVal) && int.TryParse(pgsVal, out var ps) && ps > 0)
            _pageSize = ps;

        // Фильтр
        var fltName = p(opt.FilterParamPrefix);
        if (saved.TryGetValue(fltName, out var fltVal))
        {
            var root = GridStateSerializer.DeserializeFilter(fltVal);
            if (root is not null)
                _filterRoot = root;
        }
    }

    /// <summary>
    /// Применяет строку состояния колонок (из ClayGridUserParams или URL) ПОВЕРХ дефолта
    /// из определения. Колонки, которых нет в строке состояния, сохраняют дефолтную
    /// видимость и добавляются в конец — иначе новая колонка в ЗапросыКолонки никогда
    /// не появится у пользователя с сохранённым состоянием.
    /// </summary>
    private void ApplyColumnsState(string value)
    {
        var cols = GridStateSerializer.DeserializeColumns(value);
        if (cols.Count == 0) return;

        var defOrder  = _columnOrder.ToList();
        var defHidden = _hiddenSqlNames.ToHashSet();

        _columnOrder.Clear();
        _hiddenSqlNames.Clear();

        foreach (var (sqlName, visible) in cols)
        {
            if (!_columnBySqlName.TryGetValue(sqlName, out var meta)) continue;
            if (!defOrder.Contains(meta.ColumnId)) continue;      // фильтр-онли в вывод не пускаем
            if (_columnOrder.Contains(meta.ColumnId)) continue;   // защита от дублей
            _columnOrder.Add(meta.ColumnId);
            if (visible == 0)
                _hiddenSqlNames.Add(sqlName);
        }

        // Колонки определения, которых нет в состоянии, — в конец с дефолтной видимостью
        foreach (var id in defOrder)
        {
            if (_columnOrder.Contains(id)) continue;
            _columnOrder.Add(id);
            if (_columnById.TryGetValue(id, out var meta) && defHidden.Contains(meta.SqlName))
                _hiddenSqlNames.Add(meta.SqlName);
        }

        _dataKey++;
    }

    private void ApplySavedSort(string value)
    {
        var sort = GridStateSerializer.DeserializeSort(value);
        if (sort.Count == 0) return;

        _sortState.Clear();
        _sortState.AddRange(sort);
    }

    private void ApplySavedGroups(string value)
    {
        var groups = GridStateSerializer.DeserializeGroups(value);
        if (groups.Count == 0) return;

        _groupColumns.Clear();
        foreach (var sqlName in groups)
        {
            if (_columnBySqlName.ContainsKey(sqlName))
                _groupColumns.Add(sqlName);
        }
        if (_groupColumns.Count > 0)
            _trayExpanded = true;
    }

    /// <summary>Разбирает URL-параметры фильтра и колонок, применяет к состоянию грида.</summary>
    private void ApplyUrlParams(ClayGridDynamicOptions opt)
    {
        var uri = new Uri(Nav.Uri);
        var qs  = System.Web.HttpUtility.ParseQueryString(uri.Query);

        // --- Фильтры ---
        var urlKeyToCol = _dynamicCols
            .Where(c => !string.IsNullOrEmpty(c.UrlKey))
            .ToDictionary(c => c.UrlKey!, c => c);

        var urlFilters = new List<ParsedUrlFilter>();
        foreach (string? key in qs.Keys)
        {
            if (key is null) continue;
            var cleanKey = key.StartsWith('_') ? key[1..] : key;
            if (!urlKeyToCol.TryGetValue(cleanKey, out var col)) continue;

            var desc = ClayColumnTypeMap.Resolve(col.Type);
            if (desc is null) continue;

            var rawValue = qs[key] ?? "";
            var pf = ClayGridUrlFilterParser.Parse(key, rawValue, desc);
            urlFilters.Add(pf);

            if (pf.IsForced)
                _dynamicForcedParamNames.Add(ClayGridUserParamsData.BuildParamName(opt.FilterParamPrefix, _dynamicGridId));
        }

        if (urlFilters.Count > 0)
        {
            _filterRoot ??= new ClayFilterGroupNode();
            ClayGridUrlFilterParser.Apply(_filterRoot, urlFilters, _dynamicSavedParams);
        }

        // --- Колонки (видимость/порядок) ---
        var colsParamName  = ClayGridUserParamsData.BuildParamName(opt.ColumnsParamPrefix, _dynamicGridId);
        var defColsParamName = "_" + colsParamName;

        // Forced (без '_'): применить всегда
        var forcedCols = qs[colsParamName];
        if (!string.IsNullOrEmpty(forcedCols))
        {
            _dynamicForcedParamNames.Add(colsParamName);
            ApplyColumnsState(forcedCols);
        }
        // Default (с '_'): только если нет сохранённого
        else if (!string.IsNullOrEmpty(qs[defColsParamName]) && !_dynamicSavedParams.ContainsKey(colsParamName))
        {
            ApplyColumnsState(qs[defColsParamName]!);
        }
    }

    private async Task SaveDynamicState()
    {
        var opt = DynamicOpts.Value;
        var p   = (string prefix) => ClayGridUserParamsData.BuildParamName(prefix, _dynamicGridId);

        await SaveParamIfChanged(p(opt.ColumnsParamPrefix),
            GridStateSerializer.SerializeColumns(_columnOrder, _columnById, _hiddenSqlNames), opt);
        await SaveParamIfChanged(p(opt.SortingParamPrefix),
            GridStateSerializer.SerializeSort(_sortState), opt);
        await SaveParamIfChanged(p(opt.GroupingParamPrefix),
            GridStateSerializer.SerializeGroups(_groupColumns), opt);
        await SaveParamIfChanged(p(opt.PageSizeParamPrefix),
            GridStateSerializer.SerializePageSize(_pageSize), opt);
        await SaveParamIfChanged(p(opt.FilterParamPrefix),
            GridStateSerializer.SerializeFilter(_filterRoot) ?? string.Empty, opt);
    }

    /// <summary>
    /// Пишет параметр, только если значение отличается от того, что уже в БД
    /// (по кешу <see cref="_dynamicSavedParams"/>). Forced-параметры (из URL) не сохраняются.
    /// </summary>
    private async Task SaveParamIfChanged(string name, string value, ClayGridDynamicOptions opt)
    {
        if (_dynamicForcedParamNames.Contains(name)) return;
        if (_dynamicSavedParams.TryGetValue(name, out var current) && current == value) return;

        await ClayGridUserParamsData.SaveAsync(Db, _dynamicClid, name, value, opt.UserParamsTable, opt.Schema);
        _dynamicSavedParams[name] = value;   // кеш обновляем ТОЛЬКО после успешной записи
    }
}
