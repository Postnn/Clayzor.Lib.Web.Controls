using Dapper;
using Clayzor.Lib.DALC;
using Clayzor.Lib.Entities;
using Clayzor.Lib.Web.Controls.Components.Grid.Filter;
using Clayzor.Lib.Web.Controls.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Интерфейс обратного вызова, реализуемый <see cref="ClayGridPageBase{T}"/>.
/// Позволяет <see cref="ClayGrid{TEntity}"/> уведомлять страницу об изменении запроса
/// через каскадный параметр, без явной передачи <c>OnQueryChanged</c> в разметке.
/// </summary>
public interface IClayGridDataLoader
{
    /// <summary>
    /// Вызывается гридом при изменении любого параметра запроса:
    /// поиска, сортировки, группировки, фильтрации, пагинации.
    /// </summary>
    Task OnQueryChangedAsync(ClayDataQuery query);

    /// <summary>
    /// Вызывается гридом при выборе пункта меню «Выгрузка в Excel».
    /// </summary>
    /// <param name="request">Параметры экспорта: режим, заголовок, список видимых колонок.</param>
    Task ExcelExportAsync(ExcelExportRequest request);

    /// <summary>
    /// Вызывается гридом при выборе пункта меню «Печать всех данных».
    /// Загружает все строки (без пагинации, с учётом группировки и состояния
    /// развёрнутости групп) и возвращает готовый HTML-документ для печати в iframe.
    /// </summary>
    /// <param name="columns">Видимые колонки в порядке отображения.</param>
    /// <param name="title">Заголовок грида.</param>
    /// <param name="filterDescription">Описание активных фильтров (или null).</param>
    /// <param name="groupDescription">Описание колонок группировки (или null).</param>
    Task<string> BuildPrintHtmlAsync(
        IReadOnlyList<ClayColumnMeta> columns, string title,
        string? filterDescription, string? groupDescription);

    /// <summary>
    /// Вызывается гридом при выборе пункта меню «Печать текущей страницы».
    /// Загружает те же строки, что видны на текущей странице (с учётом группировки
    /// и состояния развёрнутости групп), и возвращает готовый HTML-документ для печати.
    /// </summary>
    /// <param name="columns">Видимые колонки в порядке отображения.</param>
    /// <param name="title">Заголовок грида.</param>
    /// <param name="filterDescription">Описание активных фильтров (или null).</param>
    /// <param name="groupDescription">Описание колонок группировки (или null).</param>
    Task<string> BuildPrintHtmlForCurrentPageAsync(
        IReadOnlyList<ClayColumnMeta> columns, string title,
        string? filterDescription, string? groupDescription);

    /// <summary>
    /// Проверяет, развёрнуты ли ВСЕ группы на указанной глубине.
    /// Используется чипами панели группировки для отображения иконки переключателя.
    /// Возвращает false, если хотя бы одна группа свёрнута или группировка не активна.
    /// </summary>
    bool IsLevelFullyExpanded(int depth);

    /// <summary>
    /// Переключает состояние ВСЕХ групп на указанной глубине.
    /// При разворачивании — каскадно разворачивает родительские уровни (0..depth-1).
    /// При сворачивании — сворачивает только этот уровень.
    /// После переключения сбрасывает страницу на 1 и перезагружает данные.
    /// </summary>
    Task ToggleLevelExpandedAsync(int depth);

    /// <summary>
    /// Загружает ID всех дочерних сущностей для указанных групп (по FullKey).
    /// Используется для tri-state чекбоксов групп и массового выбора потомков.
    /// Вызывается лениво при первом клике по чекбоксу группы.
    /// </summary>
    /// <param name="groupFullKeys">FullKey групп (разделитель ).</param>
    /// <param name="query">Текущее состояние запроса (фильтры, поиск).</param>
    /// <returns>Словарь FullKey → множество ID дочерних сущностей.</returns>
    Task<Dictionary<string, HashSet<int>>> LoadGroupChildIdsAsync(
        IReadOnlyList<string> groupFullKeys, ClayDataQuery query);

    /// <summary>
    /// Загружает уникальные значения колонки для фильтра по значению (Excel-style).
    /// Выполняется лениво при открытии диалога. Ограничена порогом <paramref name="limit"/>.
    /// Фильтры <b>других</b> колонок учитываются; собственные условия колонки исключаются.
    /// Пагинация к distinct-запросу не применяется — выборка по всей таблице.
    /// </summary>
    /// <param name="sqlName">SQL-имя колонки (из реестра).</param>
    /// <param name="query">Текущее состояние запроса (поиск, фильтры, группировка).</param>
    /// <param name="limit">Максимальное количество уникальных значений (по умолчанию 100).</param>
    Task<DistinctValuesResult> LoadDistinctValuesAsync(
        string sqlName, ClayDataQuery query, int limit = 100);

    /// <summary>
    /// Вызывается гридом при выборе пункта меню «Печать выбранных».
    /// Загружает только выбранные строки (по ID) с групповыми заголовками
    /// и возвращает готовый HTML-документ для печати в iframe.
    /// </summary>
    /// <param name="columns">Видимые колонки в порядке отображения.</param>
    /// <param name="title">Заголовок грида.</param>
    /// <param name="selectedIds">ID выбранных сущностей.</param>
    /// <param name="filterDescription">Описание активных фильтров (или null).</param>
    /// <param name="groupDescription">Описание колонок группировки (или null).</param>
    Task<string> BuildPrintHtmlForSelectedAsync(
        IReadOnlyList<ClayColumnMeta> columns, string title,
        IReadOnlyList<int> selectedIds,
        string? filterDescription, string? groupDescription);
}

/// <summary>
/// Базовый класс Blazor-страницы с серверным гридом <see cref="ClayGrid{TEntity}"/>.
/// Инкапсулирует инфраструктуру загрузки данных: плоский режим и режим группировки.
/// <para>
/// Страница-наследник должна:
/// <list type="bullet">
///   <item>Унаследоваться: <c>@inherits ClayGridPageBase&lt;МояСущность&gt;</c></item>
///   <item>Передать SQL-параметры в атрибуты <c>&lt;ClayGrid&gt;</c>:
///     <c>SelectSql</c>, <c>SearchColumns</c>, <c>DefaultOrder</c>, <c>EditDialogType</c></item>
///   <item>Присвоить свойство <see cref="Grid"/> через <c>@ref</c>.</item>
///   <item>Передать <c>DataLoader="this"</c> в параметры <c>&lt;ClayGrid&gt;</c></item>
/// </list>
/// </para>
/// </summary>
/// <typeparam name="T">Тип сущности — наследник <see cref="Entity"/>.</typeparam>
public abstract partial class ClayGridPageBase<T> : ComponentBase, IClayGridDataLoader where T : Entity
{
    // ── Инжектируемые сервисы ────────────────────────────────────────────────────

    /// <summary>Менеджер подключения к БД — инжектируется автоматически.</summary>
    [Inject] protected DbManager Db { get; set; } = null!;

    /// <summary>Сервис уведомлений — инжектируется автоматически.</summary>
    [Inject] protected ISnackbar Snackbar { get; set; } = null!;

    /// <summary>Сервис диалоговых окон — инжектируется автоматически.</summary>
    [Inject] protected IDialogService DialogService { get; set; } = null!;

    /// <summary>JS interop — для скачивания файлов и других операций.</summary>
    [Inject] protected IJSRuntime JS { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;

    // ── Ссылка на грид — устанавливается через @ref="_dataGrid" ─────────────────

    /// <summary>
    /// Ссылка на грид через интерфейс <see cref="IClayGrid"/> для чтения SQL-параметров.
    /// </summary>
    protected virtual IClayGrid? Grid { get; set; }

    // ── Общее состояние страницы ─────────────────────────────────────────────────

    /// <summary>
    /// Текущее состояние запроса к данным.
    /// Обновляется в <see cref="IClayGridDataLoader.OnQueryChangedAsync"/> при каждом
    /// взаимодействии с гридом.
    /// </summary>
    protected ClayDataQuery _query = new();

    /// <summary>Строки текущей страницы (заголовки групп + строки детализации).</summary>
    protected List<IClayGridRow> _rows = [];

    /// <summary>Признак загрузки данных — управляет индикатором в <see cref="ClayGrid{TEntity}"/>.</summary>
    protected bool _loading = true;

    /// <summary>
    /// Корни дерева групп — кешируются после успешной загрузки групповых данных.
    /// null — данные не загружены или активен плоский режим.
    /// </summary>
    private List<GridGroupNode>? _groupTreeRoots;

    /// <summary>
    /// Кеш: глубина → список FullKey всех групп на этой глубине.
    /// Сбрасывается при каждой перезагрузке групповых данных.
    /// </summary>
    private Dictionary<int, List<string>>? _groupKeysByDepth;

    // ── Настройки уведомлений — могут быть переопределены на странице ─────────────

    /// <summary>Текст уведомления после успешного добавления записи.</summary>
    protected virtual string AddSuccessMessage => "Запись добавлена";

    /// <summary>Текст уведомления после успешного сохранения записи.</summary>
    protected virtual string SaveSuccessMessage => "Запись обновлена";

    // ── IClayGridDataLoader — вызывается гридом при изменении запроса ───────────

    /// <summary>
    /// Реализация <see cref="IClayGridDataLoader.OnQueryChangedAsync"/>.
    /// Копирует все поля из <paramref name="query"/> в <see cref="_query"/> и запускает загрузку.
    /// </summary>
    async Task IClayGridDataLoader.OnQueryChangedAsync(ClayDataQuery query)
    {
        _query.SearchText      = query.SearchText;
        _query.GroupEnabled    = query.GroupEnabled;
        _query.GroupColumns    = query.GroupColumns;
        _query.SortColumns     = query.SortColumns;
        _query.PageNumber      = query.PageNumber;
        _query.PageSize        = query.PageSize;
        _query.CompositeFilter = query.CompositeFilter;
        await LoadData();
    }

    async Task<string> IClayGridDataLoader.BuildPrintHtmlAsync(
        IReadOnlyList<ClayColumnMeta> columns, string title,
        string? filterDescription, string? groupDescription)
    {
        var rows = await BuildAllRowsForPrint();
        return ClayGridPrintHtmlGenerator.Build(
            title, columns, rows, typeof(T), _query.ExpandedGroups,
            filterDescription, groupDescription);
    }

    async Task<Dictionary<string, HashSet<int>>> IClayGridDataLoader.LoadGroupChildIdsAsync(
        IReadOnlyList<string> groupFullKeys, ClayDataQuery query)
    {
        var result = new Dictionary<string, HashSet<int>>();
        if (groupFullKeys.Count == 0) return result;

        var selectSql     = Grid?.SelectSql     ?? string.Empty;
        var searchColumns = Grid?.SearchColumns ?? [];

        var searchWhere    = query.BuildWhereClause(searchColumns);
        var dp             = new DynamicParameters();
        dp.Add("search", $"%{query.SearchText}%");
        var compositeWhere = BuildCompositeFilterClause(query.CompositeFilter, dp);
        var baseWhere      = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);
        var groupExprs     = query.GroupColumns;

        foreach (var fullKey in groupFullKeys)
        {
            var keys = fullKey.Split('\u001F');
            var keyParts = new List<string>();
            for (int i = 0; i < keys.Length && i < groupExprs.Count; i++)
            {
                var pName = $"gk_{fullKey.GetHashCode() & 0x7FFFFFFF}_{i}";
                dp.Add(pName, keys[i]);
                keyParts.Add($"{groupExprs[i]} = @{pName}");
            }

            if (keyParts.Count == 0) continue;

            var groupWhere    = string.Join(" AND ", keyParts);
            var combinedWhere = ClayDataQuery.CombineWhere(baseWhere, groupWhere);

            var sql = $"SELECT {_idColumnName} FROM ({selectSql}) _src";
            if (!string.IsNullOrWhiteSpace(combinedWhere))
                sql += $" WHERE {combinedWhere}";

            var ids = (await Db.QueryAsync<int>(sql, dp)).ToHashSet();
            result[fullKey] = ids;
        }

        return result;
    }

    async Task<DistinctValuesResult> IClayGridDataLoader.LoadDistinctValuesAsync(
        string sqlName, ClayDataQuery query, int limit)
    {
        // Шаг 1 — белый список
        if (!_inferredColumnTypes.ContainsKey(sqlName))
            return new DistinctValuesResult();

        // Шаг 2 — тип колонки (для обработки пустых строк)
        var colType = _inferredColumnTypes[sqlName];
        var isText  = colType == ColumnType.Text;

        // Шаг 3 — контекстный WHERE без собственных фильтров колонки
        var selectSql     = Grid?.SelectSql     ?? string.Empty;
        var searchColumns = Grid?.SearchColumns ?? [];

        var searchWhere = query.BuildWhereClause(searchColumns);
        var dp          = new DynamicParameters();
        dp.Add("search", $"%{query.SearchText}%");

        var clonedRoot     = CloneFilterTreeWithoutColumn(query.CompositeFilter, sqlName);
        var compositeWhere = BuildCompositeFilterClause(clonedRoot, dp);
        var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);
        var bracketedCol   = $"[{sqlName}]";

        // Шаг 4 — проверка на Capped
        var notBlank = isText
            ? $"{bracketedCol} IS NOT NULL AND {bracketedCol} <> ''"
            : $"{bracketedCol} IS NOT NULL";
        var valueWhere = ClayDataQuery.CombineWhere(where, notBlank);

        var countSql = $"""
            SELECT COUNT(*) FROM (
                SELECT DISTINCT {bracketedCol} v
                FROM ( {selectSql} ) src
                {(valueWhere is null ? "" : $"WHERE {valueWhere}")}
            ) t
            """;

        var distinctCount = (await Db.QueryAsync<int>(countSql, dp)).FirstOrDefault();

        // Шаг 5 — HasBlanks (один раз, дешёвый EXISTS)
        var hasBlanks = await CheckHasBlanksAsync(selectSql, bracketedCol, where, dp, isText);

        if (distinctCount > limit)
            return new DistinctValuesResult { Capped = true, HasBlanks = hasBlanks };

        // Шаг 6 — выборка значений
        var valuesSql = $"""
            SELECT DISTINCT TOP (@lim) {bracketedCol} v
            FROM ( {selectSql} ) src
            {(valueWhere is null ? "" : $"WHERE {valueWhere}")}
            ORDER BY v
            """;

        dp.Add("lim", limit);
        var rows     = await Db.QueryAsync<dynamic>(valuesSql, dp);
        var rawValues = ((IEnumerable<dynamic>)rows)
            .Select(r => (object?)((IDictionary<string, object>)r)["v"])
            .Select(v => v is DBNull ? null : v)
            .ToList();

        return new DistinctValuesResult
        {
            Values        = rawValues.AsReadOnly(),
            Capped        = false,
            HasBlanks     = hasBlanks,
            TotalDistinct = distinctCount,
        };
    }

    // ── Инфраструктура (не переопределяются на странице) ────────────────────────

    /// <summary>
    /// Загружает данные при первом рендере.
    /// Guard <c>firstRender</c> предотвращает двойную загрузку при Blazor prerendering.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Восстановление фильтра из URL при первой загрузке
            if (Grid is not null)
            {
                var uri = new Uri(Navigation.Uri);
                var filterParam = ParseQueryParam(uri.Query, Filter.ClayFilterUrlHelper.QueryParamName);
                var restored = Filter.ClayFilterUrlHelper.Deserialize(filterParam);
                if (restored is not null)
                    Grid.RestoreFilter(restored);
            }

            await LoadData();
            StateHasChanged();
        }
    }

    /// <summary>Извлекает значение query-параметра из строки запроса.</summary>
    private static string? ParseQueryParam(string query, string key)
    {
        if (string.IsNullOrEmpty(query) || query[0] != '?')
            return null;
        query = query[1..];
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            var k = eq >= 0 ? pair[..eq] : pair;
            var v = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..]) : "";
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return v;
        }
        return null;
    }

    /// <summary>
    /// Диспетчер загрузки: вызывает <see cref="LoadGroupedData"/> или <see cref="LoadFlatData"/>
    /// в зависимости от состояния группировки в <see cref="_query"/>.
    /// Управляет <see cref="_loading"/> и вызывает <c>StateHasChanged</c> по завершении.
    /// </summary>
    protected async Task LoadData()
    {
        _loading = true;
        try
        {
            if (_query.GroupEnabled && _query.GroupColumns.Count > 0)
                await LoadGroupedData();
            else
                await LoadFlatData();
        }
        catch
        {
            // DbManager автоматически передаёт SqlException в ISqlErrorHandler → ClayErrorService.
        }
        finally
        {
            _loading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Обрабатывает раскрытие/сворачивание группы в гриде.
    /// При разворачивании последней группы на странице автоматически переходит вперёд.
    /// При сворачивании группы, если страница вышла за пределы — возвращается на последнюю.
    /// </summary>
    protected async Task ToggleGroup(GroupHeaderRow header)
    {
        var wasExpanded = _query.ExpandedGroups.Contains(header.FullKey);
        if (wasExpanded)
            _query.ExpandedGroups.Remove(header.FullKey);
        else
            _query.ExpandedGroups.Add(header.FullKey);

        await LoadData();

        if (!wasExpanded)
        {
            var expandedHeader = _rows.OfType<GroupHeaderRow>()
                .FirstOrDefault(h => h.FullKey == header.FullKey);
            if (expandedHeader is not null)
            {
                var headerIdx = _rows.IndexOf(expandedHeader);
                if (headerIdx >= 0 && headerIdx == _rows.Count - 1 && header.ItemCount > 0)
                {
                    _query.PageNumber++;
                    await LoadData();
                }
            }
        }
        else if (_query.TotalCount > 0)
        {
            int maxPage = (int)Math.Ceiling((double)_query.TotalCount / _query.PageSize);
            if (_query.PageNumber > maxPage)
            {
                _query.PageNumber = maxPage;
                await LoadData();
            }
        }

        await InvokeAsync(StateHasChanged);
    }

    // ── Диалоги редактирования ───────────────────────────────────────────────────

    /// <summary>
    /// Открывает диалог добавления новой записи типа <typeparamref name="T"/>.
    /// Тип диалога берётся из параметра <c>EditDialogType</c> грида.
    /// После подтверждения показывает уведомление и перезагружает данные.
    /// </summary>
    protected async Task OpenAddDialog()
    {
        var dialogType = Grid?.EditDialogType;
        if (dialogType is null) return;

        var parameters = new DialogParameters { ["Model"] = Activator.CreateInstance<T>() };
        var options = new DialogOptionsEx { MaxWidth = MaxWidth.Small, FullWidth = true, DragMode = MudDialogDragMode.Simple };
        var dialog = await DialogService.ShowExAsync(dialogType, string.Empty, parameters, options);
        if (!(await dialog.Result)?.Canceled ?? false)
        {
            Snackbar.Add(AddSuccessMessage, Severity.Success);
            await LoadData();
        }
    }

    // ── Вспомогательные методы фильтрации ────────────────────────────────────────

    /// <summary>
    /// Возвращает множество известных SQL-имён колонок для белого списка
    /// <see cref="ClayCompositeSqlBuilder.Build"/>.
    /// Берётся из ключей <see cref="_inferredColumnTypes"/>.
    /// </summary>
    private static ISet<string> BuildKnownColumns()
        => new HashSet<string>(_inferredColumnTypes.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Строит фрагмент WHERE составного фильтра через <see cref="ClayCompositeSqlBuilder"/>
    /// и добавляет параметры в <paramref name="dp"/>.
    /// Возвращает null если дерево пустое.
    /// </summary>
    private static string? BuildCompositeFilterClause(
        ClayFilterGroupNode? filter,
        DynamicParameters dp,
        IReadOnlyDictionary<string, string>? columnNameMap = null)
        => ClayCompositeSqlBuilder.Build(filter, dp, BuildKnownColumns(), columnNameMap);

    /// <summary>
    /// Клонирует дерево фильтра, удаляя все листья (<see cref="ColumnFilter"/> и
    /// <see cref="ValueFilter"/>) с указанным <paramref name="sqlName"/>.
    /// Если группа становится пустой — она тоже удаляется.
    /// Возвращает <c>null</c> если дерево полностью опустело.
    /// </summary>
    private static ClayFilterGroupNode? CloneFilterTreeWithoutColumn(
        ClayFilterGroupNode? root, string sqlName)
    {
        if (root is null) return null;

        var filtered = new List<IClayFilterNode>();
        foreach (var node in root.Nodes)
        {
            if (node is ClayFilterGroupNode group)
            {
                var sub = CloneFilterTreeWithoutColumn(group, sqlName);
                if (sub is not null)
                    filtered.Add(sub);
            }
            else if (node is ColumnFilter cf
                     && !string.Equals(cf.Column, sqlName, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(cf.Clone());
            }
            else if (node is ValueFilter vf
                     && !string.Equals(vf.Column, sqlName, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(vf.Clone());
            }
        }

        if (filtered.Count == 0) return null;

        return new ClayFilterGroupNode
        {
            Logic = root.Logic,
            Nodes = filtered,
        };
    }

    /// <summary>
    /// Проверяет наличие NULL или пустых строк в колонке с учётом контекстного WHERE.
    /// </summary>
    private async Task<bool> CheckHasBlanksAsync(
        string selectSql, string bracketedCol, string? where, DynamicParameters dp, bool isText)
    {
        var blankCheck = isText
            ? $"{bracketedCol} IS NULL OR {bracketedCol} = ''"
            : $"{bracketedCol} IS NULL";
        var blankWhere = ClayDataQuery.CombineWhere(where, blankCheck);

        var sql = $"""
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM ( {selectSql} ) src
                {(blankWhere is null ? "" : $"WHERE {blankWhere}")}
            ) THEN 1 ELSE 0 END
            """;

        var result = await Db.QueryAsync<int>(sql, dp);
        return result.FirstOrDefault() == 1;
    }

    // ── Загрузка данных ──────────────────────────────────────────────────────────

    /// <summary>
    /// Плоский режим: загружает страницу записей с учётом поиска и фильтрации по колонкам.
    /// </summary>
    private async Task LoadFlatData()
    {
        _groupTreeRoots   = null;
        _groupKeysByDepth = null;

        var selectSql     = Grid?.SelectSql     ?? string.Empty;
        var searchColumns = Grid?.SearchColumns ?? [];
        var defaultOrder  = Grid?.DefaultOrder  ?? string.Empty;

        var searchWhere       = _query.BuildWhereClause(searchColumns);
        var orderBy           = _query.BuildOrderBy(defaultOrder);
        var dp                = new DynamicParameters();
        dp.Add("search", $"%{_query.SearchText}%");
        var compositeWhere    = BuildCompositeFilterClause(_query.CompositeFilter, dp);
        var where             = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);

        _query.TotalCount = await Entity.GetCountAsync<T>(Db, selectSql, where, dp);
        var items         = await Entity.GetPagedAsync<T>(Db, selectSql, where, orderBy, dp, _query.PageNumber, _query.PageSize);
        _rows             = items.Select(i => (IClayGridRow)new DetailRow<T> { Item = i }).ToList();
    }

    /// <summary>
    /// Режим группировки: строит агрегатный SQL, формирует дерево групп через
    /// <see cref="ClayGroupingEngine"/>, загружает видимые на текущей странице
    /// детальные строки и собирает итоговый плоский список <see cref="_rows"/>.
    /// </summary>
    private async Task LoadGroupedData()
    {
        var selectSql     = Grid?.SelectSql     ?? string.Empty;
        var searchColumns = Grid?.SearchColumns ?? [];
        var defaultOrder  = Grid?.DefaultOrder  ?? string.Empty;

        var searchWhere    = _query.BuildWhereClause(searchColumns);
        var orderBy        = _query.BuildOrderBy(defaultOrder);
        var dp             = new DynamicParameters();
        dp.Add("search", $"%{_query.SearchText}%");
        var compositeWhere = BuildCompositeFilterClause(_query.CompositeFilter, dp);
        var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);

        var exprs = _query.GroupColumns.ToList();

        var groupSql  = ClayGroupingEngine.BuildGroupAggregateSql(selectSql, exprs, where, _query.SortColumns);

        // Dapper не мапит переменный набор K{i} на GridGroupRow — читаем словарями (GN1).
        var rawGroups = await Db.QueryAsync<dynamic>(groupSql, dp);
        var groupRows = ClayGroupRowMapper.MapRows(
            rawGroups.Cast<IDictionary<string, object?>>()
                     .Select(d => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(d)),
            exprs.Count);

        var aggregates = ClayGroupingEngine.BuildAggregates(groupRows);
        var roots      = ClayGroupingEngine.BuildTree(aggregates);
        ClayGroupingEngine.ComputeParentCounts(roots);
        _groupTreeRoots    = roots;
        _groupKeysByDepth  = null;

        int totalEffective = roots.Sum(r => ClayGroupingEngine.ComputeEffectiveRows(r, _query.ExpandedGroups));
        int pageStart      = (_query.PageNumber - 1) * _query.PageSize + 1;
        int pageEnd        = _query.PageNumber * _query.PageSize;
        var layout         = new List<GridLayoutItem>();
        int cur            = 1;
        ClayGroupingEngine.WalkTree(roots, _query.ExpandedGroups, pageStart, pageEnd, ref cur, layout);

        var newRows     = new List<IClayGridRow>();
        var detailOrder = ClayGroupingEngine.BuildDetailOrder(orderBy, _query.GroupColumns, defaultOrder);

        foreach (var item in layout)
        {
            if (item.Header is not null)
                newRows.Add(item.Header);

            if (item.HasDetailRange && item.Aggregate is not null)
            {
                var ag           = item.Aggregate;
                var detailParams = new DynamicParameters();
                detailParams.AddDynamicParams(dp);

                var keyParts = ag.RawKeys
                    .Select((k, i) => { detailParams.Add($"dk{i}", k); return $"{exprs[i]} = @dk{i}"; })
                    .ToList();
                var detailWhere = ClayDataQuery.CombineWhere(where, string.Join(" AND ", keyParts));

                detailParams.Add("__start", item.DetailStart);
                detailParams.Add("__end",   item.DetailEnd);

                var sql   = ClayGroupingEngine.BuildDetailPageSql(selectSql, detailWhere, detailOrder);
                var rows  = await Db.QueryAsync<T>(sql, detailParams);
                newRows.AddRange(rows.Select(i => new DetailRow<T> { Item = i, Depth = ag.Depth }));
            }
        }

        _rows             = newRows;
        _query.TotalCount = totalEffective;
    }
}
