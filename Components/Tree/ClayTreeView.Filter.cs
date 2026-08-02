using Clayzor.Lib.Web.Controls.Components.Filter;
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

    /// <summary>Активен ли фильтр (есть хотя бы одно условие).</summary>
    private bool IsFilterActive => _filterRoot.Nodes.Count > 0;

    /// <inheritdoc/>
    bool IClayTreeView.MarksVisible => IsFilterActive;

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

    // ── Clear ────────────────────────────────────────────────────────────────────

    /// <summary>Убирает фильтр и перезагружает дерево в обычном режиме.</summary>
    private async Task ClearTreeFilterAsync()
    {
        _filterRoot = new ClayFilterGroupNode();
        await ApplyFilterAsync();
    }

    // ── Apply ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Применяет текущий фильтр. Если есть активные условия — выполняет запрос
    /// совпадений с предками и строит дерево из набора. Иначе — обычная ленивая загрузка.
    /// </summary>
    private async Task ApplyFilterAsync()
    {
        if (_filterRoot.Nodes.Count == 0)
        {
            _filterMatchCount = 0;
            _filterCapped     = false;
            await LoadRootsAsync();
            StateHasChanged();
            return;
        }

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
        if (max <= 0) max = 100; // защита от нуля

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

                // Ловушка 5: пустой результат — показать корни
                if (flatNodes.Count == 0)
                {
                    _filterMatchCount = 0;
                    _filterCapped     = false;
                    await LoadRootsAsync();
                    return;
                }

                // Подсчёт совпадений и capped
                _filterMatchCount = flatNodes.Count(n => n.IsMatch);
                _filterCapped     = _filterMatchCount > max;

                // Построить дерево из плоского списка
                BuildTreeFromFlatNodes(flatNodes);

                // Правило 1: верхний уровень выводится всегда.
                // Догружаем все корни; те, что уже в наборе — пропускаем.
                var rootResult = await _dataSource.LoadLevelAsync(new ClayTreeLoadRequest(null));
                if (rootResult.Error is null)
                {
                    foreach (var root in rootResult.Nodes)
                    {
                        if (_byId.ContainsKey(root.Id))
                            continue; // уже в фильтр-наборе

                        root.Parent = null;
                        root.Level  = 0;
                        _roots.Add(root);
                        _byId[root.Id] = root;
                    }
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
