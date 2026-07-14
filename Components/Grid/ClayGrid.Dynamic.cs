using Clayzor.Lib.DALC;
using Clayzor.Lib.Entities.DynamicGrid;
using Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Clayzor.Lib.Web.Controls.Components.Grid.Filter;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MudBlazor;
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

    private ClayGridDefinition? _dynamicDef;
    private IReadOnlyList<ClayColumnDefinition> _dynamicCols = [];
    private bool _dynamicInitDone;
    private HashSet<string> _dynamicKnownColumns = [];

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

        _dynamicInitDone = true;
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
