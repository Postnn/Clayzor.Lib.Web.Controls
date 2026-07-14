using Clayzor.Lib.DALC;
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

    // Закешированные URL/SQL действий
    private string? _dynamicEditUrl;
    private string? _dynamicNewUrl;
    private string? _dynamicDeleteSql;

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

        if (gridId == 0) return;

        _dynamicDef = await ClayGridDefinitionData.LoadGridAsync(Db, gridId, opt.SettingsTable, opt.Schema);
        if (_dynamicDef is null) return;

        Title     = _dynamicDef.Title ?? "Список";
        SelectSql = _dynamicDef.Sql;

        _dynamicCols = await ClayGridDefinitionData.LoadColumnsAsync(Db, gridId, opt.ColumnsTable, opt.Schema);

        // Видимые колонки: Order > 0, сортировка по Order
        var visibleCols = _dynamicCols
            .Where(c => c.Order is > 0)
            .OrderBy(c => c.Order ?? int.MaxValue)
            .ToList();

        SearchColumns = visibleCols.Select(c => c.Column).ToArray();
        DefaultOrder  = string.Join(", ", visibleCols.Select(c => c.Column));
        _dynamicKnownColumns = visibleCols.Select(c => c.Column).ToHashSet();

        foreach (var col in visibleCols)
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
                Filterable  = true,
                Type        = desc,
            };
            _columnById[col.ColumnId]     = meta;
            _columnBySqlName[col.Column]  = meta;
            _columnOrder.Add(col.ColumnId);

            // Кешируем имя колонки для замыкания
            var colName = col.Column;
            _cellTemplates[col.ColumnId] = (RenderFragment<CellContext<TEntity>>)(ctx =>
            {
                string text = "";
                if (ctx.Item is IReadOnlyDictionary<string, object?> dict)
                {
                    if (dict.TryGetValue(colName, out var v) && v is not null)
                        text = v.ToString()!;
                }
                return (RenderFragment)(builder => builder.AddContent(0, text));
            });
        }

        // Действия строк: резолвим URL/SQL из определения
        _dynamicEditUrl   = ClayGridLinkResolver.Resolve(_dynamicDef.EditForm, Config);
        _dynamicNewUrl    = ClayGridLinkResolver.Resolve(_dynamicDef.NewForm, Config);
        _dynamicDeleteSql = string.IsNullOrWhiteSpace(_dynamicDef.SqlDelete) ? null : _dynamicDef.SqlDelete;

        _dynamicInitDone = true;
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
        var dp = new DynamicParameters();

        var searchWhere = query.BuildWhereClause(SearchColumns);
        var filterWhere = ClayCompositeSqlBuilder.Build(query.CompositeFilter, dp, _dynamicKnownColumns);
        var where       = ClayDataQuery.CombineWhere(searchWhere, filterWhere);
        var orderBy     = query.BuildOrderBy(DefaultOrder);

        var rows = await DynamicSql.QueryPagedRowsAsync(
            Db, SelectSql, where, orderBy, dp, query.PageNumber, query.PageSize);

        TotalCount = await DynamicSql.QueryCountAsync(Db, SelectSql, where, dp);

        Items = (IEnumerable<TEntity>)rows;
    }
}
