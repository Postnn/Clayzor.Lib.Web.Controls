using Clayzor.Lib.Web.Controls.Components.Grid.Filter;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>
    /// Корень дерева фильтра — единственный источник истины.
    /// Объединяет колоночные фильтры (<c>Source=ColumnDialog</c>)
    /// и условия составного фильтра (<c>Source=CompositeDialog</c>).
    /// </summary>
    private ClayFilterGroupNode _filterRoot = new();

    /// <summary>Флаг раскрытия панели фильтрации.</summary>
    private bool _filterTrayExpanded = false;

    /// <summary>SQL-имена колонок, для которых пользователь отключил фильтр по значению через диалог настройки.</summary>
    private readonly HashSet<string> _valueFilterDisabledColumns = [];

    /// <summary>
    /// Вспомогательный доступ к листьям дерева с <c>Source=ColumnDialog</c>
    /// для отображения чипов в панели фильтрации.
    /// </summary>
    private IEnumerable<ColumnFilter> ColumnDialogLeaves =>
        _filterRoot.Nodes.OfType<ColumnFilter>()
                         .Where(f => f.Source == ClayFilterSource.ColumnDialog);

    /// <summary>Есть ли в дереве хотя бы один узел составного фильтра (не лист ColumnDialog и не ValueFilter).</summary>
    private bool HasComposite =>
        _filterRoot.Nodes.Any(n =>
            n is not ValueFilter
            && (n is not ColumnFilter cf || cf.Source != ClayFilterSource.ColumnDialog));

    /// <summary>Активные листья фильтра по значению — для отдельных чипов в панели.</summary>
    private IEnumerable<ValueFilter> ValueFilterLeaves =>
        _filterRoot.Nodes.OfType<ValueFilter>().Where(vf => vf.HasValue);

    /// <summary>
    /// Включает/выключает панель фильтрации. Настроенный фильтр при сворачивании
    /// панели сохраняется — сброс выполняется только явной кнопкой очистки.
    /// </summary>
    private Task ToggleFilterTray()
    {
        _filterTrayExpanded = !_filterTrayExpanded;
        TrayStateChanged?.Invoke();
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void OnFilterTrayDragOver(DragEventArgs e)
    {
        e.DataTransfer.DropEffect = "move";
    }

    private async Task OnFilterTrayDrop(DragEventArgs e)
    {
        var draggedSqlName = ClayDragState.DraggedColumn;
        ClayDragState.DraggedColumn = null;

        if (string.IsNullOrEmpty(draggedSqlName))
            return;
        if (!_columnBySqlName.TryGetValue(draggedSqlName, out var cm) || !cm.Filterable)
            return;

        if (HasComposite)
            await OpenCompositeFilterDialog(BuildTreeWithColumnAnded(draggedSqlName));
        else
            await OpenFilterDialog(draggedSqlName, cm.DisplayName);
    }

    /// <summary>
    /// Строит копию дерева фильтра с новым условием по колонке, приклеенным через И
    /// на верхнем уровне (сужает выборку). Если корень был <c>ИЛИ</c> — оборачивает
    /// старое дерево и новый лист в новый корень <c>И</c>, чтобы не расширить фильтр.
    /// </summary>
    private ClayFilterGroupNode BuildTreeWithColumnAnded(string sqlName)
    {
        var clone = (ClayFilterGroupNode)_filterRoot.Clone();
        var meta  = _columnBySqlName[sqlName];
        var leaf  = new ColumnFilter
        {
            Column   = sqlName,
            Operator = meta.Type.DefaultOperator,
            Source   = ClayFilterSource.CompositeDialog,
            IsNew    = true,
        };

        if (clone.Nodes.Count == 0 || clone.Logic == LogicalOperator.And)
        {
            clone.Logic = LogicalOperator.And;
            clone.Nodes.Add(leaf);
            return clone;
        }

        return new ClayFilterGroupNode
        {
            Logic = LogicalOperator.And,
            Nodes = { clone, leaf },
        };
    }

    /// <summary>
    /// Открывает диалог настройки фильтра для указанной колонки.
    /// Результат вставляется в <see cref="_filterRoot"/> как лист с <c>Source=ColumnDialog</c>.
    /// </summary>
    private async Task OpenFilterDialog(string sqlName, string displayName, ColumnFilterOperator? initialOperator = null)
    {
        // Взаимоисключение: нельзя задать условие при активном фильтре по значению
        if (_filterRoot.Nodes.OfType<ValueFilter>()
                .Any(vf => string.Equals(vf.Column, sqlName, StringComparison.OrdinalIgnoreCase) && vf.HasValue))
        {
            Snackbar.Add($"На колонке «{displayName}» установлен фильтр по значению. " +
                         "Снимите его, чтобы задать фильтр по условию.", Severity.Info);
            return;
        }

        var colType  = FilterColumnTypes.TryGetValue(sqlName, out var t) ? t : ColumnType.Text;
        // Ищем существующий лист ColumnDialog для этой колонки
        var existing = _filterRoot.Nodes
            .OfType<ColumnFilter>()
            .FirstOrDefault(f => f.Column == sqlName && f.Source == ClayFilterSource.ColumnDialog);

        var parameters = new DialogParameters<ClayColumnFilterDialog>
        {
            { x => x.ColumnDisplayName, displayName },
            { x => x.ColumnSqlName,     sqlName },
            { x => x.ColumnType,        colType },
            { x => x.ExistingFilter,    existing },
            { x => x.InitialOperator,   initialOperator },
            { x => x.LookupOptions,     FilterLookupOptions?.GetValueOrDefault(sqlName) },
        };
        var options = new DialogOptionsEx
        {
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true,
            DragMode = MudDialogDragMode.Simple,
        };
        var dialog = await DialogService.ShowExAsync<ClayColumnFilterDialog>(
            $"Фильтр: {displayName}", parameters, options);
        var result = await dialog.Result;

        if (result is not null && !result.Canceled && result.Data is ColumnFilter colFilter)
        {
            colFilter.Source = ClayFilterSource.ColumnDialog;
            // Взаимоисключение: удалить ValueFilter этой колонки
            var existingVf = _filterRoot.Nodes
                .OfType<ValueFilter>()
                .FirstOrDefault(old => string.Equals(old.Column, colFilter.Column, StringComparison.OrdinalIgnoreCase));
            if (existingVf is not null)
                _filterRoot.Nodes.Remove(existingVf);
            // Заменяем существующий или добавляем новый лист
            if (existing is not null)
            {
                var idx = _filterRoot.Nodes.IndexOf(existing);
                _filterRoot.Nodes[idx] = colFilter;
            }
            else
            {
                _filterRoot.Nodes.Add(colFilter);
            }
            _pageNumber = 1;
            await NotifyQueryChanged();
        }
    }

    /// <summary>Полностью очищает дерево фильтра и перезагружает данные.</summary>
    private async Task ClearAllFilters()
    {
        _filterRoot = new();
        _pageNumber = 1;
        await NotifyQueryChanged();
    }

    /// <summary>Удаляет листовой фильтр колонки из дерева.</summary>
    private async Task RemoveFilter(string sqlName)
    {
        var leaf = _filterRoot.Nodes
            .OfType<ColumnFilter>()
            .FirstOrDefault(f => f.Column == sqlName && f.Source == ClayFilterSource.ColumnDialog);
        if (leaf is not null)
        {
            _filterRoot.Nodes.Remove(leaf);
            _pageNumber = 1;
            await NotifyQueryChanged();
        }
    }

    /// <summary>
    /// Строит читаемое описание всего дерева фильтра для экспорта/печати.
    /// </summary>
    private string? BuildFilterDescription()
        => ClayFilterDescriptionBuilder.BuildText(
            _filterRoot,
            sqlName => _columnBySqlName.TryGetValue(sqlName, out var m) ? m.DisplayName : sqlName,
            sqlName => _columnBySqlName.TryGetValue(sqlName, out var m) ? m : null);

    /// <summary>
    /// Строит список кликабельных сегментов из всего дерева фильтра для панели.
    /// </summary>
    private IReadOnlyList<FilterSegment> BuildFilterSegments()
        => ClayFilterDescriptionBuilder.BuildSegments(
            _filterRoot,
            sqlName => _columnBySqlName.TryGetValue(sqlName, out var m) ? m.DisplayName : sqlName,
            sqlName => _columnBySqlName.TryGetValue(sqlName, out var m) ? m : null);

    /// <summary>
    /// Возвращает читаемое описание ValueFilter для отображения в чипе панели фильтрации.
    /// Делегирует в <see cref="ClayFilterDescriptionBuilder.DescribeValueFilter"/>.
    /// </summary>
    private string DescribeValueFilter(ValueFilter vf)
        => ClayFilterDescriptionBuilder.DescribeValueFilter(
            vf,
            sqlName => _columnBySqlName.TryGetValue(sqlName, out var m) ? m.DisplayName : sqlName,
            sqlName => _columnBySqlName.TryGetValue(sqlName, out var m) ? m : null);

    /// <inheritdoc cref="IClayGrid.ActiveCompositeFilter"/>
    public ClayFilterGroupNode? ActiveCompositeFilter => _filterRoot;

    /// <summary>
    /// Количество активных условий фильтра — для бейджа на кнопке.
    /// </summary>
    private int _activeFilterCount
        => ClayFilterDescriptionBuilder.CountActiveLeaves(_filterRoot);

    /// <summary>
    /// Восстанавливает дерево фильтра из внешнего источника (например, из URL).
    /// Заменяет текущий <see cref="_filterRoot"/> и оповещает подписчиков.
    /// </summary>
    public void RestoreFilter(ClayFilterGroupNode root)
    {
        _filterRoot = root;
        _ = NotifyQueryChanged();
    }

    /// <inheritdoc cref="IClayGrid.OpenCompositeFilterDialog"/>
    public async Task OpenCompositeFilterDialog() => await OpenCompositeFilterDialog(null);

    /// <summary>
    /// Открывает диалог настраиваемого фильтра. Если передан <paramref name="seedRoot"/>
    /// (например, кандидат-дерево с добавленным перетаскиванием колонки условием) —
    /// диалог открывается на нём вместо действующего <see cref="_filterRoot"/>;
    /// отмена диалога не меняет действующий фильтр.
    /// </summary>
    private async Task OpenCompositeFilterDialog(ClayFilterGroupNode? seedRoot)
    {
        // Фильтруемые колонки — все зарегистрированные Filterable (включая сгруппированные,
        // чтобы диалог мог разрешить display-name для колонок, которые и сгруппированы, и фильтруются)
        var filterableCols = _columnBySqlName.Values
            .Where(c => c.Filterable)
            .ToList();

        var parameters = new DialogParameters<ClayFilterDialog>
        {
            { x => x.Root,         seedRoot ?? _filterRoot },
            { x => x.Columns,      (IReadOnlyList<ClayColumnMeta>)filterableCols },
            { x => x.LookupOptions, FilterLookupOptions },
        };
        var options = new DialogOptionsEx
        {
            MaxWidth  = MaxWidth.Small,
            FullWidth = false,
            CloseOnEscapeKey = true,
            DragMode  = MudDialogDragMode.Simple,
        };
        var dialog = await DialogService.ShowExAsync<ClayFilterDialog>(
            "Настраиваемый фильтр", parameters, options);
        var result = await dialog.Result;

        if (result is not null && !result.Canceled && result.Data is ClayFilterGroupNode newRoot)
        {
            _filterRoot = newRoot;
            _pageNumber = 1;
            await NotifyQueryChanged();
        }
    }

    // ── Фильтр по значению (Excel-style) — V7 ──────────────────────────────────

    /// <inheritdoc cref="IClayGrid.IsValueFilterAvailable"/>
    bool IClayGrid.IsValueFilterAvailable(string sqlName)
    {
        if (!EnableValueFilter) return false;
        if (!_columnBySqlName.TryGetValue(sqlName, out var meta)) return false;
        return meta.Filterable && meta.AllowValueFilter
               && !_valueFilterDisabledColumns.Contains(sqlName);
    }

    /// <inheritdoc cref="IClayGrid.IsValueFilterActive"/>
    bool IClayGrid.IsValueFilterActive(string sqlName)
        => _filterRoot.Nodes.OfType<ValueFilter>()
            .Any(vf => string.Equals(vf.Column, sqlName, StringComparison.OrdinalIgnoreCase) && vf.HasValue);

    /// <inheritdoc cref="IClayGrid.OpenValueFilterDialog"/>
    async Task IClayGrid.OpenValueFilterDialog(string sqlName)
    {
        if (!_columnBySqlName.TryGetValue(sqlName, out var meta)) return;

        var colType = FilterColumnTypes.TryGetValue(sqlName, out var t) ? t : ColumnType.Text;

        var existingValue = _filterRoot.Nodes
            .OfType<ValueFilter>()
            .FirstOrDefault(vf => string.Equals(vf.Column, sqlName, StringComparison.OrdinalIgnoreCase));

        var existingCondition = _filterRoot.Nodes
            .OfType<ColumnFilter>()
            .FirstOrDefault(cf => cf.Column == sqlName && cf.Source == ClayFilterSource.ColumnDialog);

        // Ленивый загрузчик (замыкание на LoadDistinctValuesAsync).
        // В динамическом режиме DataLoader отсутствует — используем собственный запрос.
        Func<Task<DistinctValuesResult>> load = Dynamic
            ? () => LoadDistinctValuesDynamicAsync(sqlName, BuildCurrentQuery(), 100)
            : () => DataLoader!.LoadDistinctValuesAsync(sqlName, BuildCurrentQuery(), 100);

        var parameters = new DialogParameters<ClayColumnValueFilterDialog>
        {
            { x => x.ColumnSqlName,            sqlName },
            { x => x.ColumnDisplayName,        meta.DisplayName },
            { x => x.ColumnType,               colType },
            { x => x.BoolTrueLabel,            meta.BoolTrueLabel },
            { x => x.BoolFalseLabel,           meta.BoolFalseLabel },
            { x => x.ExistingValueFilter,      existingValue },
            { x => x.ExistingConditionFilter,  existingCondition },
            { x => x.LoadValues,               load },
        };
        var options = new DialogOptionsEx
        {
            MaxWidth  = MaxWidth.ExtraSmall,
            FullWidth = true,
            DragMode  = MudDialogDragMode.Simple,
        };
        var dialog = await DialogService.ShowExAsync<ClayColumnValueFilterDialog>(
            $"Фильтр: {meta.DisplayName}", parameters, options);
        var result = await dialog.Result;

        if (result is null || result.Canceled || result.Data is null) return;

        switch (result.Data)
        {
            case ValueFilter vf:
                await ApplyValueFilter(sqlName, vf);
                break;

            case OpenConditionRequest ocr:
                await OpenFilterDialog(sqlName, meta.DisplayName, ocr.Operator);
                break;

            default:
                if (ReferenceEquals(result.Data, ClayColumnValueFilterDialog.ClearedSentinel))
                {
                    await RemoveValueFilter(sqlName);
                }
                else if (ReferenceEquals(result.Data, ClayColumnValueFilterDialog.RemoveConditionSentinel))
                {
                    await RemoveFilter(sqlName);
                    await ((IClayGrid)this).OpenValueFilterDialog(sqlName);
                }
                break;
        }
    }

    /// <summary>
    /// Применяет ValueFilter к дереву. Удаляет существующие ColumnFilter и ValueFilter
    /// для этой колонки (взаимоисключение — треб. 8, 9).
    /// </summary>
    private async Task ApplyValueFilter(string sqlName, ValueFilter vf)
    {
        // Удалить существующий ColumnFilter (Source=ColumnDialog) для этой колонки
        var existingCond = _filterRoot.Nodes
            .OfType<ColumnFilter>()
            .FirstOrDefault(cf => cf.Column == sqlName && cf.Source == ClayFilterSource.ColumnDialog);
        if (existingCond is not null)
            _filterRoot.Nodes.Remove(existingCond);

        // Удалить старый ValueFilter для этой колонки
        var existingVf = _filterRoot.Nodes
            .OfType<ValueFilter>()
            .FirstOrDefault(old => string.Equals(old.Column, sqlName, StringComparison.OrdinalIgnoreCase));
        if (existingVf is not null)
            _filterRoot.Nodes.Remove(existingVf);

        _filterRoot.Nodes.Add(vf);
        _pageNumber = 1;
        await NotifyQueryChanged();
    }

    /// <summary>Удаляет ValueFilter для указанной колонки из дерева фильтра.</summary>
    private async Task RemoveValueFilter(string sqlName)
    {
        var existingVf = _filterRoot.Nodes
            .OfType<ValueFilter>()
            .FirstOrDefault(old => string.Equals(old.Column, sqlName, StringComparison.OrdinalIgnoreCase));
        if (existingVf is not null)
        {
            _filterRoot.Nodes.Remove(existingVf);
            _pageNumber = 1;
            await NotifyQueryChanged();
        }
    }

    /// <summary>
    /// Динамическая реализация <see cref="IClayGridDataLoader.LoadDistinctValuesAsync"/>
    /// для value-filter. Повторяет логику <c>ClayGridPageBase.LoadDistinctValuesAsync</c>,
    /// но работает с <see cref="DynamicSql"/> и собственным <c>SelectSql</c>.
    /// </summary>
    private async Task<DistinctValuesResult> LoadDistinctValuesDynamicAsync(
        string sqlName, ClayDataQuery query, int limit)
    {
        var selectSql     = SelectSql ?? string.Empty;
        var searchColumns = SearchColumns ?? [];
        var meta          = _columnBySqlName[sqlName];
        var isText        = meta.Type.Kind == ColumnType.Text;

        var searchWhere = query.BuildWhereClause(searchColumns);
        var dp          = new Dapper.DynamicParameters();
        dp.Add("search", $"%{query.SearchText}%");

        var clonedRoot     = CloneFilterTreeWithoutColumn(query.CompositeFilter, sqlName);
        var compositeWhere = ClayCompositeSqlBuilder.Build(clonedRoot, dp, _dynamicKnownColumns);
        var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);
        var bracketedCol   = $"[{sqlName}]";

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

        var blankCheck = isText
            ? $"{bracketedCol} IS NULL OR {bracketedCol} = ''"
            : $"{bracketedCol} IS NULL";
        var blankWhere = ClayDataQuery.CombineWhere(where, blankCheck);
        var blankSql = $"""
            SELECT CASE WHEN EXISTS(
                SELECT 1 FROM ( {selectSql} ) src
                {(blankWhere is null ? "" : $"WHERE {blankWhere}")}
            ) THEN 1 ELSE 0 END
            """;
        var hasBlanks = (await Db.QueryAsync<int>(blankSql, dp)).FirstOrDefault() == 1;

        if (distinctCount > limit)
            return new DistinctValuesResult { Capped = true, HasBlanks = hasBlanks };

        var valuesSql = $"""
            SELECT DISTINCT TOP (@lim) {bracketedCol} v
            FROM ( {selectSql} ) src
            {(valueWhere is null ? "" : $"WHERE {valueWhere}")}
            ORDER BY v
            """;
        dp.Add("lim", limit);
        var rows      = await Db.QueryAsync<dynamic>(valuesSql, dp);
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

    /// <summary>
    /// Клонирует дерево фильтра, исключая все узлы (<see cref="ColumnFilter"/> и
    /// <see cref="ValueFilter"/>), относящиеся к указанной колонке. Группы без узлов
    /// после фильтрации отбрасываются.
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
    /// Строит снимок текущего состояния запроса для передачи в
    /// <see cref="IClayGridDataLoader.LoadDistinctValuesAsync"/>.
    /// </summary>
    private ClayDataQuery BuildCurrentQuery()
        => new()
        {
            SearchText      = _searchText ?? "",
            CompositeFilter = _filterRoot,
        };

}
