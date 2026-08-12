using Clayzor.Lib.DALC;
using Clayzor.Lib.Entities;
using Clayzor.Lib.Entities.DynamicGrid;
using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Clayzor.Lib.Web.Controls.Components.Filter;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;
using Dapper;
using System.Diagnostics;
using System.Web;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Динамический режим ClayGrid: грид загружает определение (SQL, колонки, кнопки)
/// и пользовательские параметры из БД вместо статической разметки.
/// </summary>
public partial class ClayGrid<TEntity> where TEntity : class
{
    [Inject] private DbManager Db { get; set; } = default!;
    [Inject] private IOptions<ClayGridDynamicSettings> DynamicOpts { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Inject] private IConfiguration Config { get; set; } = default!;

    private ClayGridDefinition? _dynamicDef;
    private IReadOnlyList<ClayColumnDefinition> _dynamicCols = [];
    /// <summary>Identity последней динамической инициализации. null — никогда не инициализировался или статический режим (CGFR1).</summary>
    private ClayGridDynamicKey? _currentDynamicKey;
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
    private HashSet<string> _dynamicQuickSearchCols = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _quickSearchEffective = [];

    /// <summary>URI, для которого разобран <see cref="_queryCache"/> (CGFR1 §6).</summary>
    private string? _queryCacheUri;
    private System.Collections.Specialized.NameValueCollection? _queryCache;

    /// <summary>
    /// Кеш разобранной query-строки — единая точка разбора URL для всех резолверов.
    /// URI-aware: смена <see cref="NavigationManager.Uri"/> автоматически переразбирает строку (CGFR1 §6).
    /// </summary>
    private System.Collections.Specialized.NameValueCollection Query
    {
        get
        {
            var uri = Nav.Uri;
            if (!string.Equals(_queryCacheUri, uri, StringComparison.Ordinal))
            {
                _queryCacheUri = uri;
                _queryCache = System.Web.HttpUtility.ParseQueryString(new Uri(uri).Query);
            }
            return _queryCache;
        }
    }

    /// <summary>Снапшот дефолтной раскладки колонок — для сброса при применении shared-настроек.</summary>
    private List<int> _defaultColumnOrder = [];
    private HashSet<string> _defaultHiddenNames = [];

    /// <summary>
    /// Единственный владелец инициализации динамического режима (CGFR1).
    /// Выполняется при первом рендере и при каждом обновлении параметров.
    /// <see cref="OnParametersSet"/> (sync) отрабатывает раньше и уже установил _opt.
    /// Строит value-based identity, сравнивает с предыдущей: та же — без reinit;
    /// изменилась — сброс старого runtime + инициализация нового.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        if (!_opt.Dynamic)
        {
            // true → false: старый dynamic runtime не должен управлять static grid (CGFR1 §23)
            if (_currentDynamicKey is not null)
            {
                _currentDynamicKey = null;
                ResetDynamicRuntimeState();
            }
            return;
        }

        var opt = DynamicOpts.Value;
        var key = ClayGridDynamicKey.Create(
            ResolveDynamicGridId(opt), ResolveClientId(opt), ResolveSharedId(), opt);

        if (key == _currentDynamicKey)
            return; // та же identity — без повторной загрузки definition/columns/данных (CGFR1 §18)

        ResetDynamicRuntimeState();
        try
        {
            await InitDynamicMode();
            _currentDynamicKey = key;   // только после normal completion (CGFR1.1)
        }
        catch
        {
            _currentDynamicKey = null;  // разблокировать retry той же identity (CGFR1.1)
            throw;
        }
    }

    /// <summary>
    /// Полная инвалидация dynamic runtime старого грида ДО инициализации нового (CGFR1 §8–§17).
    /// Очищает definition-dependent состояние, колонки, lookup-ы, действия, фильтры/группировку/сортировку,
    /// выделение, строки, счётчик, ошибку, shared-режим, dynamic grouping state.
    /// <see cref="_queryCache"/> НЕ очищается — <see cref="Query"/> теперь URI-aware (CGFR1 §6).
    /// </summary>
    private void ResetDynamicRuntimeState()
    {
        // ── Identity / definition (Dynamic.cs) ──
        _dynamicGridId = 0;
        _dynamicClid = 0;
        _dynamicDef = null;
        _dynamicCols = [];
        _dynamicKnownColumns.Clear();
        _dynamicLookups.Clear();
        _dynamicIconLookups.Clear();

        // ── Действия (CGFR1 §13) ──
        _dynamicEditUrl = null;
        _dynamicNewUrl = null;
        _dynamicDeleteSql = null;

        // ── Персистенция / поиск ──
        _dynamicSavedParams.Clear();
        _dynamicForcedParamNames.Clear();
        _dynamicQuickSearchCols.Clear();
        _quickSearchEffective = [];
        _defaultColumnOrder.Clear();
        _defaultHiddenNames.Clear();

        // ── Ошибка (CGFR1 §12) ──
        _dynamicError = null;

        // ── Shared-режим ──
        _isSharedMode = false;
        _hasSharedSettings = false;
        _sharedList.Clear();
        _sharedListLoading = false;

        // ── Definition-derived option overrides (перезапишутся при init B) ──
        _opt.Title = "Список";
        _opt.SelectSql = "";
        _opt.SearchColumns = [];
        _opt.DefaultOrder = "";

        // ── Definition-dependent column state (CGFR1 §9, §29) ──
        _columnById.Clear();
        _columnBySqlName.Clear();
        _columnOrder.Clear();
        _hiddenSqlNames.Clear();
        _cellTemplates.Clear();

        // ── Строки / счётчик (CGFR1 §11) ──
        Items = [];
        TotalCount = 0;
        _lastQuery = new ClayDataQuery();

        // ── Query/UI state (CGFR1 §10) ──
        _searchText = null;
        _sortState.Clear();
        _groupColumns.Clear();
        _trayExpanded = false;
        _groupChildIds.Clear();
        _filterRoot = new ClayFilterGroupNode();
        _filterTrayExpanded = false;
        _valueFilterDisabledColumns.Clear();
        _pageNumber = 1;
        _pageSize = _opt.PageSize;
        _selectMode = false;
        _selectAllChecked = false;
        _selectedIds.Clear();

        // ── Динамическая группировка (Dynamic.Grouping.cs) ──
        _dynamicExpandedGroups.Clear();
        _dynamicGroupRoots = null;
        _dynamicGroupKeysByDepth = null;
        _dynamicGroupWhere = null;
        _dynamicGroupParams = null;
        _dynamicGroupExprs = [];

        _dataKey++;
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

        // SH8: разбор sharedId — если невалидный, отказ до загрузки данных
        var sharedId = ResolveSharedId();
        _isSharedMode = sharedId.HasValue;
        if (sharedId is int id && id <= 0)
        {
            _dynamicError = $"Неверный код общей настройки «{id}» — ссылка недействительна.";
            return;
        }

        _dynamicDef = await ClayGridDefinitionData.LoadGridWithQuickSearchAsync(
            Db, gridId, opt.SettingsTable, opt.ColumnsTable, opt.Schema);
        if (_dynamicDef is null)
        {
            // Если ошибка connectivity — оверлей переподключения всё покажет,
            // не выводим ложное «Грид не найден»
            if (ErrorService.IsCurrentErrorConnectivity)
                return;
            _dynamicError = $"Грид не найден: запрос №{gridId} отсутствует в «{opt.SettingsTable}».";
            return;
        }

        _opt.Title     = _dynamicDef.Title ?? "Список";
        _opt.SelectSql = _dynamicDef.Sql;

        _dynamicCols = await ClayGridDefinitionData.LoadColumnsAsync(
            Db, gridId, opt.ColumnsTable, opt.Schema,
            supportsQuickSearch: _dynamicDef.SupportsQuickSearch);

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

        _opt.SearchColumns = visibleCols.Select(c => c.Column).ToArray();
        _opt.DefaultOrder  = string.Join(", ", visibleCols.Select(c => c.Column));
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
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ClayGrid] Справочник колонки '{col.Column}' не загружен: {ex.Message}");
                }
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
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ClayGrid] Справочник иконки '{col.Column}' не загружен: {ex.Message}");
                }
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

            var kind = (ClayColumnKind)col.Type;

            // Value-filter (выбор из списка уникальных значений) осмыслен для
            // «плоских» атомарных значений. Исключены: List (5 — справочник),
            // Link/Html/Icon (4/8/9 — не атомарные), фильтр-онли (6/11).
            var allowValueFilter =
                kind is ClayColumnKind.Number or ClayColumnKind.Text or ClayColumnKind.Date
                     or ClayColumnKind.Bool   or ClayColumnKind.DateTimeLocal
                     or ClayColumnKind.TimeLocal or ClayColumnKind.LimitedText;

            var meta = new ClayColumnMeta
            {
                ColumnId         = col.ColumnId,
                SqlName          = col.Column,
                DisplayName      = col.Header ?? col.Column,
                SortName         = col.Column,
                Groupable        = true,
                Filterable       = col.Type != (int)ClayColumnKind.List,
                AllowValueFilter = allowValueFilter,
                Type             = desc,
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

        // Снапшот дефолтной раскладки колонок — для сброса при применении shared-настроек.
        _defaultColumnOrder = _columnOrder.ToList();
        _defaultHiddenNames = _hiddenSqlNames.ToHashSet();

        // Восстановление сохранённого состояния пользователя
        await RestoreDynamicState(opt);

        // Вычислить итоговый набор колонок быстрого поиска
        await RefreshQuickSearchEffective(opt);

        // Применить URL-параметры (фильтры и колонки)
        ApplyUrlParams(opt);

        // В динамике value-filter по умолчанию выключен у всех колонок.
        // Включается per-column через переключатель в диалоге «Настройка колонок».
        foreach (var meta in _columnById.Values)
        {
            if (meta.AllowValueFilter)
                _valueFilterDisabledColumns.Add(meta.SqlName);
        }

        // SH8: загрузить и применить общие настройки ДО первой загрузки данных
        if (_isSharedMode && sharedId.HasValue && sharedId.Value > 0)
        {
            var sharedParams = await LoadAndValidateSharedParamsAsync(sharedId.Value, opt);
            if (sharedParams is not null)
                await ApplySharedParams(sharedParams, opt);
            // Если null — _dynamicError уже установлен, грид не загрузится
        }
        else
        {
            // Обычный режим — проверить наличие своих общих настроек (SH7)
            await CheckSharedSettingsAsync();
        }

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

        if (_opt.Dynamic)
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

    private int ResolveDynamicGridId(ClayGridDynamicSettings opt)
    {
        if (_opt.DynamicGridId.HasValue && _opt.DynamicGridId.Value != 0)
            return _opt.DynamicGridId.Value;

        var val = Query[opt.GridIdQueryParam];
        return val is not null && int.TryParse(val, out var gid) ? gid : 0;
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

            // Быстрый поиск: строим WHERE с учётом типов колонок, CAST/CONVERT и ESCAPE
            string? searchWhere = null;
            if (_opt.SearchColumns is { Length: > 0 } && !string.IsNullOrWhiteSpace(query.SearchText))
            {
                var escapedText = EscapeLikePattern(query.SearchText);
                dp.Add("q", $"%{escapedText}%");

                var colByName = _dynamicCols.ToDictionary(c => c.Column, c => c, StringComparer.OrdinalIgnoreCase);
                var exprs = _opt.SearchColumns
                    .Select(col => colByName.TryGetValue(col, out var def)
                        ? BuildSearchLikeExpr(col, def.Type, def.Format)
                        : $"{col} LIKE @q ESCAPE '\\'")
                    .ToList();
                searchWhere = exprs.Count > 0 ? $"({string.Join(" OR ", exprs)})" : null;
            }
            else if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                // Строка поиска есть, но SearchColumns пуст — убираем условие поиска
                query.SearchText = null;
            }

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

        var orderBy = query.BuildOrderBy(_opt.DefaultOrder, AllowedOrderExpressions());

        TotalCount = await DynamicSql.QueryCountAsync(Db, _opt.SelectSql, where, dp);

        // Кламп страницы: после сужающего фильтра PageNumber мог уйти за диапазон
        var totalPages = query.PageSize > 0 && TotalCount > 0
            ? (int)Math.Ceiling((double)TotalCount / query.PageSize) : 1;
        if (query.PageNumber > totalPages)
        {
            query.PageNumber = totalPages;
            _pageNumber      = totalPages;
        }
        if (query.PageNumber < 1)
        {
            query.PageNumber = 1;
            _pageNumber      = 1;
        }

        var rows = await DynamicSql.QueryPagedRowsAsync(
            Db, _opt.SelectSql, where, orderBy, dp, query.PageNumber, query.PageSize);
        Items = rows.Select(r => (TEntity)(object)new ClayDynamicRow(r)).ToList();
    }

    // ── Персистенция состояния ─────────────────────────────────────────────────

    private int ResolveClientId(ClayGridDynamicSettings opt)
        => int.TryParse(Query[opt.ClientIdQueryParam], out var clid) ? clid : 0;

    /// <summary>
    /// Разбирает sharedId из URL.
    /// null — параметра нет или он равен 0 (обычный режим).
    /// Положительное — валидный sharedId.
    /// Отрицательное/нечисловое — ошибка.
    /// </summary>
    private int? ResolveSharedId()
    {
        var val = Query[ClayShareUrlBuilder.SharedIdParam];
        if (string.IsNullOrEmpty(val)) return null;
        if (!int.TryParse(val, out var sid)) return -1; // не число → ошибка
        if (sid == 0) return null;                       // 0 = обычный режим
        return sid;
    }

    /// <summary>
    /// Загружает параметры общей настройки через <c>UserParamsShared</c>
    /// и проверяет соответствие имён текущему гриду.
    /// При ошибке устанавливает <see cref="_dynamicError"/> и возвращает null.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>?> LoadAndValidateSharedParamsAsync(
        int sharedId, ClayGridDynamicSettings opt)
    {
        var paramNames = ClayGridParamRegistry.GetGridParamNames(opt, _dynamicGridId);
        IReadOnlyDictionary<string, string> sharedParams;
        try
        {
            sharedParams = await ClayGridSharedParamsData.LoadSharedParamsAsync(
                Db, sharedId, opt.UserParamsShared, paramNames);
        }
        catch
        {
            _dynamicError = $"Не удалось загрузить общие настройки №{sharedId}. " +
                            "Ссылка недействительна или база данных недоступна.";
            return null;
        }

        if (sharedParams.Count == 0)
        {
            _dynamicError = $"Общие настройки №{sharedId} не найдены. " +
                            "Возможно, ссылка устарела или была удалена.";
            return null;
        }

        if (!ClaySharedParamValidator.IsValid(sharedParams.Keys, paramNames))
        {
            _dynamicError = $"Общие настройки №{sharedId} не соответствуют текущему гриду. " +
                            "Ссылка могла быть создана для другого набора данных.";
            return null;
        }

        return sharedParams;
    }

    /// <summary>
    /// Применяет чужие параметры к состоянию грида теми же методами десериализации,
    /// что и <see cref="RestoreDynamicState"/>. НЕ заполняет <see cref="_dynamicSavedParams"/>
    /// (кеш «что в БД») — чтобы choke point не думал, что параметры уже сохранены.
    /// Перед применением сбрасывает ВСЁ состояние к дефолтам — чужая ссылка должна
    /// давать воспроизводимый результат, а не гибрид личных и чужих настроек.
    /// </summary>
    private async Task ApplySharedParams(IReadOnlyDictionary<string, string> sharedParams, ClayGridDynamicSettings opt)
    {
        // Сброс к дефолтам: снимаем личные настройки, накопленные RestoreDynamicState.
        _sortState.Clear();
        _groupColumns.Clear();
        _filterRoot = new ClayFilterGroupNode();
        _dynamicQuickSearchCols.Clear();
        _trayExpanded = false;
        ResetColumnsToDefinitionDefault();
        _pageSize = _opt.PageSize;

        var p = (string prefix) => ClayGridUserParamsData.BuildParamName(prefix, _dynamicGridId);

        // Колонки
        var colsName = p(opt.ColumnsParamPrefix);
        if (sharedParams.TryGetValue(colsName, out var colsVal))
            ApplyColumnsState(colsVal);

        // Сортировка
        var srtName = p(opt.SortingParamPrefix);
        if (sharedParams.TryGetValue(srtName, out var srtVal))
            ApplySavedSort(srtVal);

        // Группировка
        var grpName = p(opt.GroupingParamPrefix);
        if (sharedParams.TryGetValue(grpName, out var grpVal))
            ApplySavedGroups(grpVal);

        // Размер страницы
        var pgsName = p(opt.PageSizeParamPrefix);
        if (sharedParams.TryGetValue(pgsName, out var pgsVal) && int.TryParse(pgsVal, out var ps) && ps > 0)
            _pageSize = ps;

        // Фильтр
        var fltName = p(opt.FilterParamPrefix);
        if (sharedParams.TryGetValue(fltName, out var fltVal))
        {
            var root = GridStateSerializer.DeserializeFilter(fltVal);
            if (root is not null)
                _filterRoot = root;
        }

        // Быстрый поиск
        var qksName = p(opt.QuickSearchParamPrefix);
        if (sharedParams.TryGetValue(qksName, out var qksVal))
            ApplySavedQuickSearch(qksVal);

        await RefreshQuickSearchEffective(opt);
    }

    /// <summary>Переходит на текущий грид без sharedId (полная перезагрузка).</summary>
    private void OpenWithoutSharedId()
    {
        var cleanUrl = ClayShareUrlBuilder.BuildShareUrl(Nav.Uri, DynamicOpts.Value.GridIdQueryParam, 0);
        // Убираем sharedId из чистого URL (BuildShareUrl добавляет sharedId=0)
        var uri = new Uri(cleanUrl);
        var baseUrl = uri.GetLeftPart(UriPartial.Path);
        var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
        qs.Remove(ClayShareUrlBuilder.SharedIdParam);
        var gridIdVal = qs[DynamicOpts.Value.GridIdQueryParam];
        qs.Clear();
        if (gridIdVal is not null)
            qs[DynamicOpts.Value.GridIdQueryParam] = gridIdVal;
        var finalUrl = qs.Count > 0 ? $"{baseUrl}?{qs}" : baseUrl;
        Nav.NavigateTo(finalUrl, forceLoad: true);
    }

    private async Task RestoreDynamicState(ClayGridDynamicSettings opt)
    {
        var p = (string prefix) => ClayGridUserParamsData.BuildParamName(prefix, _dynamicGridId);
        var paramNames = new[] {
            p(opt.ColumnsParamPrefix), p(opt.FilterParamPrefix),
            p(opt.GroupingParamPrefix), p(opt.SortingParamPrefix), p(opt.PageSizeParamPrefix),
            p(opt.QuickSearchParamPrefix)
        };

        var saved = await ClayGridUserParamsData.LoadAsync(
            Db, _dynamicClid, paramNames, opt.UserParamsTable, opt.Schema, sharedId: 0);

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

        // Быстрый поиск
        var qksName = p(opt.QuickSearchParamPrefix);
        if (saved.TryGetValue(qksName, out var qksVal))
            ApplySavedQuickSearch(qksVal);
    }

    /// <summary>
    /// Разбирает сохранённый список колонок быстрого поиска (имена через запятую).
    /// Игнорирует имена, которых больше нет в определении колонок (регистронезависимо).
    /// </summary>
    private void ApplySavedQuickSearch(string value)
    {
        _dynamicQuickSearchCols.Clear();
        if (string.IsNullOrWhiteSpace(value)) return;

        var known = _dynamicCols.ToDictionary(c => c.Column, c => c, StringComparer.OrdinalIgnoreCase);
        foreach (var name in value.Split(','))
        {
            var trimmed = name.Trim();
            if (trimmed.Length > 0 && known.ContainsKey(trimmed))
                _dynamicQuickSearchCols.Add(known[trimmed].Column); // каноническое имя из определения
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
        // Белый список SortName всех зарегистрированных колонок — защита от инъекции
        // через shared-ссылку (значение параметра контролируется автором ссылки).
        var sort = GridStateSerializer.DeserializeSort(value, AllowedOrderExpressions());
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
            // Белый список: только зарегистрированные группируемые колонки (Groupable=true).
            if (_columnBySqlName.TryGetValue(sqlName, out var meta) && meta.Groupable)
                _groupColumns.Add(sqlName);
        }
        if (_groupColumns.Count > 0)
            _trayExpanded = true;
    }

    /// <summary>Белый список выражений, допустимых в ORDER BY: SortName всех зарегистрированных колонок.</summary>
    private ISet<string> AllowedOrderExpressions()
        => _columnBySqlName.Values.Select(m => m.SortName).ToHashSet(StringComparer.Ordinal);

    /// <summary>Сбрасывает порядок и видимость колонок к дефолту определения грида.</summary>
    private void ResetColumnsToDefinitionDefault()
    {
        _columnOrder.Clear();
        _columnOrder.AddRange(_defaultColumnOrder);
        _hiddenSqlNames.Clear();
        foreach (var n in _defaultHiddenNames) _hiddenSqlNames.Add(n);
        _dataKey++;
    }

    /// <summary>Разбирает URL-параметры фильтра и колонок, применяет к состоянию грида.</summary>
    private void ApplyUrlParams(ClayGridDynamicSettings opt)
    {

        // --- Фильтры ---
        var urlKeyToCol = _dynamicCols
            .Where(c => !string.IsNullOrEmpty(c.UrlKey))
            .ToDictionary(c => c.UrlKey!, c => c);

        var urlFilters = new List<ParsedUrlFilter>();
        foreach (string? key in Query.Keys)
        {
            if (key is null) continue;
            var cleanKey = key.StartsWith('_') ? key[1..] : key;
            if (!urlKeyToCol.TryGetValue(cleanKey, out var col)) continue;

            var desc = ClayColumnTypeMap.Resolve(col.Type);
            if (desc is null) continue;

            var rawValue = Query[key] ?? "";
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
        var forcedCols = Query[colsParamName];
        if (!string.IsNullOrEmpty(forcedCols))
        {
            _dynamicForcedParamNames.Add(colsParamName);
            ApplyColumnsState(forcedCols);
        }
        // Default (с '_'): только если нет сохранённого
        else if (!string.IsNullOrEmpty(Query[defColsParamName]) && !_dynamicSavedParams.ContainsKey(colsParamName))
        {
            ApplyColumnsState(Query[defColsParamName]!);
        }
    }

    private async Task SaveDynamicState()
    {
        // SH8: choke point — в режиме sharedId личные параметры не сохраняются.
        if (_isSharedMode) return;

        var opt = DynamicOpts.Value;
        var p   = (string prefix) => ClayGridUserParamsData.BuildParamName(prefix, _dynamicGridId);

        var candidates = new List<(string Name, string Value)>
        {
            (p(opt.ColumnsParamPrefix),  GridStateSerializer.SerializeColumns(_columnOrder, _columnById, _hiddenSqlNames)),
            (p(opt.SortingParamPrefix),  GridStateSerializer.SerializeSort(_sortState)),
            (p(opt.GroupingParamPrefix), GridStateSerializer.SerializeGroups(_groupColumns)),
            (p(opt.PageSizeParamPrefix), GridStateSerializer.SerializePageSize(_pageSize)),
            (p(opt.FilterParamPrefix),   GridStateSerializer.SerializeFilter(_filterRoot) ?? string.Empty),
        };
        var qksValue = SerializeQuickSearchColumns();
        if (qksValue is not null)
            candidates.Add((p(opt.QuickSearchParamPrefix), qksValue));

        var toSave = candidates
            .Where(c => !_dynamicForcedParamNames.Contains(c.Name))
            .Where(c => !(_dynamicSavedParams.TryGetValue(c.Name, out var cur) && cur == c.Value))
            .ToList();

        if (toSave.Count == 0) return;

        await ClayGridUserParamsData.SaveManyAsync(Db, _dynamicClid, toSave, opt.UserParamsTable, opt.Schema, sharedId: 0);

        foreach (var (name, value) in toSave)
            _dynamicSavedParams[name] = value;
    }

    /// <summary>
    /// Пересчитывает <see cref="_quickSearchEffective"/> и обновляет <see cref="SearchColumns"/>.
    /// Возвращает <c>true</c>, если была выполнена перезагрузка данных
    /// (поиск активен и набор колонок изменился).
    /// </summary>
    internal async Task<bool> RefreshQuickSearchEffective(ClayGridDynamicSettings opt)
    {
        var qksUserParam = _dynamicSavedParams.TryGetValue(
            ClayGridUserParamsData.BuildParamName(opt.QuickSearchParamPrefix, _dynamicGridId),
            out var qksVal) ? qksVal : null;

        var oldSet = _quickSearchEffective;
        _quickSearchEffective = ComputeEffectiveQuickSearchColumns(
            _dynamicDef?.SupportsQuickSearch ?? false, _dynamicCols, qksUserParam);

        if (_dynamicDef?.SupportsQuickSearch == true)
            _opt.SearchColumns = _quickSearchEffective.ToArray();

        // Поиск неактивен — только обновить SearchColumns, без перезагрузки
        if (string.IsNullOrWhiteSpace(_searchText))
            return false;

        // Набор опустел при активном поиске — очистить строку
        if (_quickSearchEffective.Count == 0)
            _searchText = null;

        // Набор изменился при активном поиске — перезагрузить данные
        var setChanged = oldSet.Count != _quickSearchEffective.Count
            || !oldSet.SequenceEqual(_quickSearchEffective, StringComparer.OrdinalIgnoreCase);
        if (setChanged)
        {
            _pageNumber = 1;
            await NotifyQueryChanged();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Сериализует набор колонок быстрого поиска в строку (имена через запятую).
    /// Если строка длиннее 1000 символов — показывает Snackbar и возвращает null
    /// (сохранение пропускается).
    /// </summary>
    private string? SerializeQuickSearchColumns()
    {
        if (_dynamicQuickSearchCols.Count == 0)
            return string.Empty;

        var value = string.Join(",", _dynamicQuickSearchCols);
        if (value.Length <= 1000)
            return value;

        Snackbar.Add("Слишком много колонок для быстрого поиска — настройка не сохранена", Severity.Warning);
        return null;
    }

    /// <summary>
    /// Экранирует метасимволы LIKE в пользовательском вводе:
    /// <c>%</c> → <c>\%</c>, <c>_</c> → <c>\_</c>, <c>[</c> → <c>\[</c>.
    /// </summary>
    public static string EscapeLikePattern(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");

    /// <summary>
    /// Строит SQL-выражение LIKE для одной колонки быстрого поиска
    /// с приведением типа и экранированием (SQL Server 2008 R2).
    /// </summary>
    /// <param name="column">Имя колонки (выходное имя SELECT).</param>
    /// <param name="type">Код типа колонки (<see cref="ClayColumnKind"/>).</param>
    /// <param name="format">Строка формата из БД (для дат — .NET-формат, напр. "dd.MM.yyyy").</param>
    /// <returns>Выражение LIKE с CAST/CONVERT и ESCAPE.</returns>
    public static string BuildSearchLikeExpr(string column, int type, string? format)
    {
        // Дата без времени: CONVERT(nvarchar(30), col, 104) — формат dd.mm.yyyy
        if (type == (int)ClayColumnKind.Date)
            return $"CONVERT(nvarchar(30), {column}, 104) LIKE @q ESCAPE '\\'";

        // Дата+время / время: CONVERT(nvarchar(30), col, 121) — формат yyyy-mm-dd hh:mi:ss
        if (type == (int)ClayColumnKind.DateTimeLocal || type == (int)ClayColumnKind.TimeLocal)
            return $"CONVERT(nvarchar(30), {column}, 121) LIKE @q ESCAPE '\\'";

        // Число (int/long/decimal): CAST в строку
        if (type == (int)ClayColumnKind.Number)
            return $"CAST({column} AS nvarchar(50)) LIKE @q ESCAPE '\\'";

        // Текст и остальные — напрямую
        return $"{column} LIKE @q ESCAPE '\\'";
    }

    /// <summary>
    /// Вычисляет итоговый набор имён колонок быстрого поиска из трёх источников
    /// в строгом порядке приоритета:
    /// 1. <paramref name="supportsQuickSearch"/>=false → пусто (колонки нет в таблице).
    /// 2. Админский набор: колонки с <c>QuickSearch=true</c>.
    /// 3. Пользовательский набор: null=нет строки→админский; "" (пусто)→перебивает;
    ///    "col1,col2"→только перечисленные (регистронезависимо).
    /// 4. Фильтр по допустимости типа (<see cref="ClayColumnKindExtensions.SupportsQuickSearch"/>):
    ///    недопустимый тип исключается независимо от источника.
    /// Метод чистый — без БД, покрывается тестами.
    /// </summary>
    /// <param name="supportsQuickSearch">Колонка УчаствуетВБыстромПоиске есть в таблице.</param>
    /// <param name="allColumns">Все колонки определения грида.</param>
    /// <param name="userParam">Сохранённое значение пользователя (null — нет строки).</param>
    /// <returns>Итоговый список имён колонок (может быть пустым).</returns>
    public static IReadOnlyList<string> ComputeEffectiveQuickSearchColumns(
        bool supportsQuickSearch,
        IReadOnlyList<ClayColumnDefinition> allColumns,
        string? userParam)
    {
        if (!supportsQuickSearch)
            return [];

        if (userParam is null)
        {
            // Нет пользовательской настройки — берём админский набор (QuickSearch=true + допустимый тип)
            return allColumns
                .Where(c => c.QuickSearch && ClayColumnKindExtensions.SupportsQuickSearch(c.Type))
                .Select(c => c.Column)
                .ToList();
        }

        // Пользовательская настройка есть (в т.ч. пустая строка) — перебивает админский набор
        if (string.IsNullOrWhiteSpace(userParam))
            return [];

        var known = allColumns.ToDictionary(c => c.Column, c => c, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var name in userParam.Split(','))
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0) continue;
            if (!known.TryGetValue(trimmed, out var col)) continue;         // нет в определении
            if (!ClayColumnKindExtensions.SupportsQuickSearch(col.Type)) continue; // недопустимый тип
            result.Add(col.Column); // каноническое имя
        }
        return result;
    }

    // ── SH5: «Поделиться» ────────────────────────────────────────────────────

    /// <summary>
    /// Собирает текущее состояние грида в словарь Параметр → Значение,
    /// используя ту же сериализацию, что и <see cref="SaveDynamicState"/>.
    /// Параметры с пустым значением исключаются — восстановят состояние по умолчанию.
    /// </summary>
    private IReadOnlyDictionary<string, string> BuildCurrentParamSet()
    {
        var opt = DynamicOpts.Value;
        var p   = (string prefix) => ClayGridUserParamsData.BuildParamName(prefix, _dynamicGridId);

        var result = new Dictionary<string, string>
        {
            [p(opt.ColumnsParamPrefix)]     = GridStateSerializer.SerializeColumns(_columnOrder, _columnById, _hiddenSqlNames),
            [p(opt.SortingParamPrefix)]     = GridStateSerializer.SerializeSort(_sortState),
            [p(opt.GroupingParamPrefix)]    = GridStateSerializer.SerializeGroups(_groupColumns),
            [p(opt.PageSizeParamPrefix)]    = GridStateSerializer.SerializePageSize(_pageSize),
            [p(opt.FilterParamPrefix)]      = GridStateSerializer.SerializeFilter(_filterRoot) ?? string.Empty,
        };

        var qksValue = SerializeQuickSearchColumns();
        if (qksValue is not null)
            result[p(opt.QuickSearchParamPrefix)] = qksValue;

        // Параметры с пустым значением не пишем — восстановят состояние по умолчанию
        return result.Where(kv => kv.Value.Length > 0)
                     .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>Обработчик кнопки «Поделиться»: диалог → создание общей настройки.</summary>
    private async Task OpenShareDialog()
    {
        var parameters = new DialogParameters<ClayShareDialog>
        {
            { x => x.InitialValue, _opt.Title }
        };
        var options = new DialogOptionsEx { DragMode = MudDialogDragMode.Simple };
        var dialog = await DialogService.ShowExAsync<ClayShareDialog>("Поделиться настройками", parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled) return;

        var title = result.Data as string;
        if (string.IsNullOrWhiteSpace(title)) return;

        await CreateSharedLinkAsync(title);
    }

    /// <summary>
    /// Создаёт общую настройку с текущим состоянием грида, формирует ссылку
    /// и копирует в буфер обмена. На время операции — оверлей через <see cref="RunBusyAsync"/>.
    /// </summary>
    private async Task CreateSharedLinkAsync(string title)
    {
        try
        {
            await RunBusyAsync("Создание ссылки…", async () =>
            {
                var opt    = DynamicOpts.Value;
                var @params = BuildCurrentParamSet();
                if (@params.Count == 0)
                {
                    Snackbar.Add("Нет параметров для сохранения", Severity.Warning);
                    return;
                }

                var sharedId = await ClayGridSharedParamsData.CreateWithParamsAsync(
                    Db, title, @params,
                    opt.UserSharedParamsTable, opt.UserParamsTable, opt.Schema);

                // Сборка URL: белый список — только gridId, плюс sharedId (SH6)
                var sharedUrl = ClayShareUrlBuilder.BuildShareUrl(
                    Nav.Uri, opt.GridIdQueryParam, sharedId);

                // Копирование в буфер с проверкой результата
                var copied = await JS.InvokeAsync<bool>(
                    "clayGridShare.copyToClipboard", new object[] { sharedUrl });

                if (copied)
                {
                    Snackbar.Add("Ссылка скопирована в буфер обмена", Severity.Success);
                    _hasSharedSettings = true; // только что создали первую или очередную
                }
                else
                {
                    // Буфер недоступен (http без localhost) — показываем ссылку
                    // для ручного копирования. Длинный таймаут — пользователь копирует сам.
                    Snackbar.Add(sharedUrl, Severity.Info, config =>
                    {
                        config.RequireInteraction = true;
                        config.VisibleStateDuration = 30000;
                    });
                }
            });
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // DbManager уже передал ошибку в ISqlErrorHandler → ClayErrorBar.
            // Не роняем circuit — пользователь увидит баннер с деталями.
        }
    }

    // ── SH7: список общих настроек ──────────────────────────────────────────

    /// <summary>Признак, что грид открыт по чужой ссылке (sharedId в URL). В этом режиме
    /// кнопки «Поделиться» и списка общих настроек скрыты.</summary>
    private bool _isSharedMode;

    /// <summary>Признак наличия общих настроек у текущего грида (управляет видимостью кнопки).</summary>
    private bool _hasSharedSettings;

    /// <summary>Список общих настроек: (КодНастройкиОбщей, Название).</summary>
    private List<(int SharedId, string Title)> _sharedList = [];

    /// <summary>Признак загрузки списка (показывается MudProgressCircular).</summary>
    private bool _sharedListLoading;

    /// <summary>Ссылка на меню списка общих настроек — для программного закрытия.</summary>
    private ClayMenu _sharedListMenu = null!;

    /// <summary>Проверяет наличие общих настроек и обновляет <see cref="_hasSharedSettings"/>.</summary>
    private async Task CheckSharedSettingsAsync()
    {
        var opt = DynamicOpts.Value;
        var paramNames = ClayGridParamRegistry.GetGridParamNames(opt, _dynamicGridId);
        _hasSharedSettings = await ClayGridSharedParamsData.AnyAsync(
            Db, _dynamicClid, paramNames, opt.UserParamsTable, opt.Schema);
    }

    /// <summary>Загружает список общих настроек из БД. Вызывается при раскрытии меню.</summary>
    private async Task LoadSharedListAsync()
    {
        _sharedListLoading = true;
        try
        {
            var opt = DynamicOpts.Value;
            var paramNames = ClayGridParamRegistry.GetGridParamNames(opt, _dynamicGridId);
            var items = await ClayGridSharedParamsData.ListAsync(
                Db, _dynamicClid, paramNames, opt.UserParamsTable, opt.UserSharedParamsTable, opt.Schema);
            _sharedList = items.ToList();
            if (_sharedList.Count == 0)
                _hasSharedSettings = false;
        }
        catch
        {
            // Ошибка загрузки — оставляем предыдущий список, не роняем меню
            _sharedList = [];
        }
        finally
        {
            _sharedListLoading = false;
        }
    }

    /// <summary>Открывает диалог переименования общей настройки.</summary>
    private async Task RenameSharedAsync(int sharedId, string currentTitle)
    {
        await _sharedListMenu.CloseAsync();
        await Task.Delay(100); // даём меню закрыться перед открытием диалога

        var parameters = new DialogParameters<ClayShareDialog>
        {
            { x => x.Title, "Переименовать" },
            { x => x.InitialValue, currentTitle },
            { x => x.ActionButtonText, "Сохранить" }
        };
        var options = new DialogOptionsEx { DragMode = MudDialogDragMode.Simple };
        var dialog = await DialogService.ShowExAsync<ClayShareDialog>("Переименовать", parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled) return;

        var newTitle = result.Data as string;
        if (string.IsNullOrWhiteSpace(newTitle)) return;

        var opt = DynamicOpts.Value;
        await ClayGridSharedParamsData.RenameAsync(Db, sharedId, newTitle, opt.UserSharedParamsTable);
    }

    /// <summary>Удаляет общую настройку с подтверждением.</summary>
    private async Task DeleteSharedAsync(int sharedId, string title)
    {
        await _sharedListMenu.CloseAsync();
        await Task.Delay(100);

        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Message, $"Удалить общую настройку «{title}»?" }
        };
        var options = new DialogOptionsEx { DragMode = MudDialogDragMode.Simple };
        var dialog = await DialogService.ShowExAsync<ConfirmDialog>("Подтверждение", parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled) return;

        var opt = DynamicOpts.Value;
        await ClayGridSharedParamsData.DeleteAsync(
            Db, sharedId, opt.UserParamsTable, opt.UserSharedParamsTable, opt.Schema);
        await CheckSharedSettingsAsync();
    }

    /// <summary>Копирует ссылку общей настройки в буфер обмена.</summary>
    private async Task CopySharedLinkAsync(int sharedId)
    {
        await _sharedListMenu.CloseAsync();
        await Task.Delay(100);

        var opt = DynamicOpts.Value;
        var sharedUrl = ClayShareUrlBuilder.BuildShareUrl(Nav.Uri, opt.GridIdQueryParam, sharedId);
        var copied = await JS.InvokeAsync<bool>(
            "clayGridShare.copyToClipboard", new object[] { sharedUrl });

        if (copied)
        {
            Snackbar.Add("Ссылка скопирована в буфер обмена", Severity.Success);
        }
        else
        {
            Snackbar.Add(sharedUrl, Severity.Info, config =>
            {
                config.RequireInteraction = true;
                config.VisibleStateDuration = 30000;
            });
        }
    }

    /// <summary>Строит URL для кнопки «Перейти» — открывается в новом окне.</summary>
    private string BuildSharedUrl(int sharedId)
    {
        var opt = DynamicOpts.Value;
        return ClayShareUrlBuilder.BuildShareUrl(Nav.Uri, opt.GridIdQueryParam, sharedId);
    }
}
