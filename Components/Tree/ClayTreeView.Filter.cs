using Clayzor.Lib.Entities.Tree;
using Clayzor.Lib.Web.Controls.Components.Filter;
using Clayzor.Lib.Web.Controls.Components.Grid;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Clayzor.Lib.Web.Controls.Components.Tree.DataSources;
using Clayzor.Lib.Web.Controls.Components.Tree.Helpers;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Dapper;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    // ── Injects ──────────────────────────────────────────────────────────────────

    [Inject] private IDialogService DialogService { get; set; } = default!;

    // ── Fields ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Корень дерева фильтра — единственный источник истины.
    /// Пустая группа (Nodes.Count == 0) означает «фильтр не установлен».
    /// </summary>
    private ClayFilterGroupNode _filterRoot = new();

    /// <summary>SqlName колонок, для которых заданы FilterDefaults — для определения «только дефолтный».</summary>
    private HashSet<string> _defaultFilterColumns = [];

    /// <summary>Активен ли фильтр (есть хотя бы одно условие).</summary>
    private bool IsFilterActive => _filterRoot.Nodes.Count > 0;

    /// <summary>Только дефолтный фильтр: все активные условия — нетронутые значения из FilterDefaults.</summary>
    private bool _isDefaultOnly;

    /// <inheritdoc/>
    bool IClayTreeView.MarksVisible => IsFilterActive && !_isDefaultOnly;

    /// <summary>Количество активных условий — для бейджа на кнопке.</summary>
    private int ActiveFilterCount => ClayFilterDescriptionBuilder.CountActiveLeaves(_filterRoot);

    /// <summary>Число совпадений фильтра (нод с IsMatch=true).</summary>
    private int _filterMatchCount;

    /// <summary>Сработал ли лимит MaxFilterRecords.</summary>
    private bool _filterCapped;

    /// <summary>Идёт выполнение запроса фильтра — для индикатора загрузки в панели.</summary>
    private bool _isFiltering;

    // ── BuildFilterColumns ───────────────────────────────────────────────────────

    /// <summary>
    /// Строит список фильтруемых полей дерева для диалога настраиваемого фильтра.
    /// Колонки из <see cref="ClayTreeOptions.FilterExcludedColumns"/> исключаются.
    /// Возвращает пустой список, если <see cref="ClayTreeOptions.FilterColumns"/> не задан.
    /// </summary>
    private IReadOnlyList<ClayFilterColumnInfo> BuildFilterColumns()
        => ClayTreeFilterColumnBuilder.Build(Options.FilterColumns, Options.FilterExcludedColumns);

    /// <summary>Ищет DisplayName колонки по SqlName в FilterColumns.</summary>
    private string GetFilterColumnDisplayName(string sqlName)
    {
        if (Options.FilterColumns is { Count: > 0 } cols)
        {
            foreach (var col in cols)
            {
                if (string.Equals(col.SqlName, sqlName, StringComparison.OrdinalIgnoreCase))
                    return col.DisplayName;
            }
        }
        return sqlName;
    }

    // ── Tooltip ──────────────────────────────────────────────────────────────────

    /// <summary>Текстовое представление фильтра для tooltip кнопки.</summary>
    private string? FilterTooltip => ClayFilterDescriptionBuilder.BuildText(
        _filterRoot, GetFilterColumnDisplayName);

    // ── Open dialog ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Открывает диалог настраиваемого фильтра дерева.
    /// Результат заменяет <see cref="_filterRoot"/> и (в TF_E) перезагружает данные по фильтру.
    /// На этом шаге — просто перезагружает дерево в обычном режиме.
    /// </summary>
    private async Task OpenTreeFilterDialogAsync()
    {
        var cols = BuildFilterColumns();
        if (cols.Count == 0)
            return;

        var parameters = new DialogParameters<ClayFilterDialog>
        {
            { x => x.Root,    _filterRoot.Nodes.Count > 0 ? _filterRoot : new ClayFilterGroupNode() },
            { x => x.Columns, (IReadOnlyList<ClayFilterColumnInfo>)cols },
        };
        var options = new DialogOptionsEx
        {
            MaxWidth         = MaxWidth.Medium,
            FullWidth        = false,
            CloseOnEscapeKey = true,
            DragMode         = MudDialogDragMode.Simple,
        };
        var dialog = await DialogService.ShowExAsync<ClayFilterDialog>(
            "Настраиваемый фильтр", parameters, options);
        var result = await dialog.Result;

        if (result is not null && !result.Canceled && result.Data is ClayFilterGroupNode newRoot)
        {
            // Пустой результат (сброс) — трактовать как «фильтра нет»
            _filterRoot = newRoot.Nodes.Count == 0 ? new ClayFilterGroupNode() : newRoot;
            await ApplyFilterAsync();
        }
    }

    // ── Query parameters ─────────────────────────────────────────────────────────

    /// <summary>
    /// Применяет фильтр из query-параметров URL. Вызывается при инициализации.
    /// Формат — как у грида: UrlKey=op~value (forced) или _UrlKey=op~value (default).
    /// Переиспользует <see cref="ClayGridUrlFilterParser.Parse"/>.
    /// </summary>
    public void ApplyQueryFilter(string queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
            return;

        if (Options.FilterColumns is not { Count: > 0 })
            return;

        // Строим urlKey → колонка (как в гриде)
        var urlKeyToCol = Options.FilterColumns
            .Where(c => !string.IsNullOrEmpty(c.UrlKey))
            .ToDictionary(c => c.UrlKey!, StringComparer.OrdinalIgnoreCase);

        if (urlKeyToCol.Count == 0)
            return;

        var qs = queryString.StartsWith('?') ? queryString[1..] : queryString;
        var pairs = qs.Split('&', StringSplitOptions.RemoveEmptyEntries);

        var filterCols = BuildFilterColumns();
        var colBySqlName = filterCols.ToDictionary(c => c.SqlName, StringComparer.OrdinalIgnoreCase);

        var hasForced = false;
        var forcedRoot = new ClayFilterGroupNode();
        var defaultRoot = new ClayFilterGroupNode();

        foreach (var pair in pairs)
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx < 0) continue;

            var rawName  = Uri.UnescapeDataString(pair[..eqIdx]);
            var rawValue = Uri.UnescapeDataString(pair[(eqIdx + 1)..]);
            if (string.IsNullOrEmpty(rawValue)) continue;

            var cleanKey = rawName.StartsWith('_') ? rawName[1..] : rawName;
            if (!urlKeyToCol.TryGetValue(cleanKey, out var col))
                continue;

            if (!colBySqlName.TryGetValue(col.SqlName, out var colInfo))
                continue;

            var parsed = ClayGridUrlFilterParser.Parse(rawName, rawValue, colInfo.Type);

            var leaf = new ColumnFilter
            {
                Column   = col.SqlName,
                Operator = parsed.Operator,
                Value    = parsed.Value,
                Source   = ClayFilterSource.CompositeDialog,
            };

            if (parsed.IsDefault)
                defaultRoot.Nodes.Add(leaf);
            else
            {
                forcedRoot.Nodes.Add(leaf);
                hasForced = true;
            }
        }

        if (hasForced)
        {
            _filterRoot = forcedRoot;
            _defaultFilterColumns = [];
        }
        else if (_filterRoot.Nodes.Count == 0 && defaultRoot.Nodes.Count > 0)
        {
            _filterRoot = defaultRoot;
            _defaultFilterColumns = [];
        }
    }

    // ── Clear ────────────────────────────────────────────────────────────────────

    /// <summary>Убирает фильтр и перезагружает дерево в обычном режиме.</summary>
    private async Task ClearTreeFilterAsync()
    {
        _filterRoot = new ClayFilterGroupNode();
        await ApplyFilterAsync();
    }

    // ── Default filter detection ─────────────────────────────────────────────────

    /// <summary>
    /// Определяет, является ли текущий _filterRoot «только дефолтным»:
    /// все активные листья — из _defaultFilterColumns, и их значения не менялись.
    /// </summary>
    private bool ComputeIsDefaultOnly()
    {
        if (_filterRoot.Nodes.Count == 0)
            return false;

        var filterCols = BuildFilterColumns();
        var colBySqlName = new Dictionary<string, ClayTreeFilterColumn>(StringComparer.OrdinalIgnoreCase);
        if (Options.FilterColumns is not null)
        {
            foreach (var c in Options.FilterColumns)
                colBySqlName[c.SqlName] = c;
        }

        foreach (var node in _filterRoot.Nodes)
        {
            if (node is not ColumnFilter leaf || !leaf.HasValue)
                continue;

            if (!_defaultFilterColumns.Contains(leaf.Column))
                return false;

            // Проверить, что значение не изменилось относительно FilterDefaults
            if (!Options.FilterDefaults.TryGetValue(leaf.Column, out var defVal))
                return false;

            if (!Equals(leaf.Value?.ToString(), defVal?.ToString()))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Строит WHERE из FilterDefaults для тихого ограничения ленивой загрузки.
    /// Возвращает (whereClause, parameters) или null если дефолтов нет.
    /// </summary>
    private (string? whereClause, DynamicParameters? dp) BuildDefaultWhere()
    {
        if (Options.FilterDefaults is not { Count: > 0 })
            return (null, null);

        var filterCols = BuildFilterColumns();
        var colBySqlName = filterCols.ToDictionary(c => c.SqlName, StringComparer.OrdinalIgnoreCase);

        var defRoot = new ClayFilterGroupNode();
        _defaultFilterColumns = [];

        foreach (var (sqlName, value) in Options.FilterDefaults)
        {
            if (value is null && colBySqlName.TryGetValue(sqlName, out var ci)
                && ci.Type.Kind != ColumnType.Text)
                continue;

            var op = colBySqlName.TryGetValue(sqlName, out var col) && col.Type.DefaultOperator != default
                ? col.Type.DefaultOperator
                : ColumnFilterOperator.Contains;

            defRoot.Nodes.Add(new ColumnFilter
            {
                Column   = sqlName,
                Operator = op,
                Value    = value,
                Source   = ClayFilterSource.CompositeDialog,
            });

            _defaultFilterColumns.Add(sqlName);
        }

        if (defRoot.Nodes.Count == 0)
            return (null, null);

        var knownColumns = new HashSet<string>(filterCols.Select(c => c.SqlName), StringComparer.OrdinalIgnoreCase);
        var dp = new DynamicParameters();
        var whereClause = ClayCompositeSqlBuilder.Build(defRoot, dp, knownColumns);
        return (whereClause, dp);
    }

    /// <summary>
    /// Инициализирует дефолтный фильтр при старте (до первой загрузки данных).
    /// Строит _filterRoot из FilterDefaults и помечает их дефолтными.
    /// </summary>
    public void InitializeDefaultFilter()
    {
        if (Options.FilterDefaults is not { Count: > 0 })
            return;

        var filterCols = BuildFilterColumns();
        var colBySqlName = filterCols.ToDictionary(c => c.SqlName, StringComparer.OrdinalIgnoreCase);

        _defaultFilterColumns = [];
        _filterRoot = new ClayFilterGroupNode();

        foreach (var (sqlName, value) in Options.FilterDefaults)
        {
            var op = colBySqlName.TryGetValue(sqlName, out var col) && col.Type.DefaultOperator != default
                ? col.Type.DefaultOperator
                : ColumnFilterOperator.Contains;

            _filterRoot.Nodes.Add(new ColumnFilter
            {
                Column   = sqlName,
                Operator = op,
                Value    = value,
                Source   = ClayFilterSource.CompositeDialog,
            });

            _defaultFilterColumns.Add(sqlName);
        }

        _isDefaultOnly = _filterRoot.Nodes.Count > 0;
    }

    // ── Apply ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Применяет текущий фильтр. Три режима:
    /// нет условий → обычная ленивая загрузка;
    /// только дефолтные → тихий WHERE в ленивом режиме (без пометок/счётчика);
    /// пользовательские → полный режим фильтра с предками и пометками.
    /// </summary>
    private async Task ApplyFilterAsync()
    {
        if (_filterRoot.Nodes.Count == 0)
        {
            _isDefaultOnly     = false;
            _filterMatchCount  = 0;
            _filterCapped      = false;
            UpdateSourceExtraWhere(null);
            await LoadRootsAsync();
            StateHasChanged();
            return;
        }

        _isDefaultOnly = ComputeIsDefaultOnly();

        if (_isDefaultOnly)
        {
            // Дефолтный режим: тихий WHERE, без пометок/счётчика/предков
            _filterMatchCount = 0;
            _filterCapped     = false;
            var (defWhere, _) = BuildDefaultWhere();
            UpdateSourceExtraWhere(defWhere);
            await LoadRootsAsync();
            StateHasChanged();
            return;
        }

        // Пользовательский режим: полный фильтр (TF_E)
        UpdateSourceExtraWhere(null);

        var filterCols = BuildFilterColumns();
        var knownColumns = new HashSet<string>(
            filterCols.Select(c => c.SqlName), StringComparer.OrdinalIgnoreCase);

        var dp = new DynamicParameters();
        var whereClause = ClayCompositeSqlBuilder.Build(_filterRoot, dp, knownColumns);

        if (whereClause is null)
        {
            _filterMatchCount = 0;
            _filterCapped     = false;
            await LoadRootsAsync();
            StateHasChanged();
            return;
        }

        var max = Options.MaxFilterRecords;
        if (max <= 0) max = 100;

        _isFiltering = true;
        StateHasChanged();

        try
        {
            await RunBusyAsync("Поиск…", async () =>
            {
                var result = await _dataSource.LoadFilteredAsync(whereClause, dp, max);
                if (result.Error is not null)
                {
                    _error = result.Error;
                    await OnLoadError.InvokeAsync(result.Error);
                    _roots.Clear();
                    _byId.Clear();
                    return;
                }

                var flatNodes = result.Nodes;

                if (flatNodes.Count == 0)
                {
                    _filterMatchCount = 0;
                    _filterCapped     = false;
                    await LoadRootsAsync();
                    return;
                }

                _filterMatchCount = flatNodes.Count(n => n.IsMatch);
                _filterCapped     = _filterMatchCount > max;

                BuildTreeFromFlatNodes(flatNodes);

                var rootResult = await _dataSource.LoadLevelAsync(new ClayTreeLoadRequest(null));
                if (rootResult.Error is null)
                {
                    foreach (var root in rootResult.Nodes)
                    {
                        if (_byId.ContainsKey(root.Id))
                            continue;

                        root.Parent = null;
                        root.Level  = 0;
                        _roots.Add(root);
                        _byId[root.Id] = root;
                    }

                    // Восстановить сортировку корней по L (NestedSet) или Text (ParentKey)
                    SortRoots();
                }

                await SaveStateAsync();
            });
        }
        finally
        {
            _isFiltering = false;
            StateHasChanged();
        }
    }

    /// <summary>Обновляет ExtraWhere в _source и пересоздаёт _dataSource при изменении.</summary>
    private void UpdateSourceExtraWhere(string? extraWhere)
    {
        if (_source is null)
            return;

        if (_source.ExtraWhere == extraWhere)
            return;

        _source = new ClayTreeSource(
            Options.SelectSql,
            Options.HierarchyMode,
            Options.Schema,
            Options.OrderBy,
            Options.RootId,
            _source.PageSize,
            _source.Cursor,
            extraWhere);

        _dataSource = DataSource ?? new ClaySqlTreeDataSource(ResolveDb(), _source);
    }

    /// <summary>Сортирует _roots по Left (NestedSet) или Text (ParentKey).</summary>
    private void SortRoots()
    {
        if (_roots.Count <= 1) return;

        if (Options.HierarchyMode == ClayTreeHierarchyMode.NestedSet)
            _roots.Sort((a, b) => (a.Left ?? 0).CompareTo(b.Left ?? 0));
        else
            _roots.Sort((a, b) => string.CompareOrdinal(a.Text, b.Text));
    }

    /// <summary>
    /// Строит дерево из плоского списка узлов фильтра. Группирует по ParentId,
    /// проставляет Parent/Level/Children. Заменяет <see cref="_roots"/> и <see cref="_byId"/>.
    /// </summary>
    private void BuildTreeFromFlatNodes(IReadOnlyList<ClayTreeNode> flatNodes)
    {
        _roots.Clear();
        _byId.Clear();

        // Индекс всех узлов по Id
        foreach (var node in flatNodes)
            _byId[node.Id] = node;

        // Группировка по ParentId — дети к родителям
        var byParent = new Dictionary<string, List<ClayTreeNode>>();
        foreach (var node in flatNodes)
        {
            var parentKey = ClaySqlTreeDataSource.ToKey(node.ParentId);
            if (!byParent.TryGetValue(parentKey, out var children))
            {
                children = [];
                byParent[parentKey] = children;
            }
            children.Add(node);
        }

        // Корни: узлы, чей ParentId не в наборе (пустая строка или не найден среди flatNodes)
        foreach (var node in flatNodes)
        {
            var parentKey = ClaySqlTreeDataSource.ToKey(node.ParentId);
            if (parentKey.Length == 0 || !_byId.ContainsKey(parentKey))
            {
                node.Parent = null;
                node.Level  = 0;
                _roots.Add(node);
            }
        }

        // Привязка детей к родителям
        foreach (var (parentKey, children) in byParent)
        {
            if (parentKey.Length == 0 || !_byId.TryGetValue(parentKey, out var parentNode))
                continue;

            foreach (var child in children)
            {
                child.Parent = parentNode;
                child.Level  = parentNode.Level + 1;
                parentNode.Children.Add(child);
            }

            // Нода с детьми из фильтр-набора — неполный уровень
            if (parentNode.Children.Count > 0)
            {
                parentNode.ChildrenAreFiltered = true;
                parentNode.HasChildren = true;
            }
        }
    }
}
