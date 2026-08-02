using Clayzor.Lib.Entities.Tree;
using Clayzor.Lib.Web.Controls.Components.Tree.DataSources;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    /// <summary>Плоский индекс всех загруженных узлов по строковому ключу.</summary>
    private readonly Dictionary<string, ClayTreeNode> _byId = [];

    /// <summary>Оверлей .clay-busy активен.</summary>
    private bool _isBusy;

    /// <summary>Подпись под индикатором загрузки.</summary>
    private string? _busyLabel;

    /// <summary>Выполняет операцию под оверлеем .clay-busy, если <see cref="ClayTreeOptions.ShowBusyOverlay"/>.</summary>
    private async Task RunBusyAsync(string label, Func<Task> work)
    {
        if (Options.ShowBusyOverlay)
        {
            _busyLabel = label;
            _isBusy    = true;
            StateHasChanged();
            await Task.Delay(100);  // дать Blazor отрендерить оверлей до операции
        }
        else
        {
            // Без оверлея: дать рендеру отрисовать per-node спиннер (CTF5) или спиннер хвоста (CTP2)
            await Task.Yield();
        }

        try
        {
            await work();
        }
        finally
        {
            if (Options.ShowBusyOverlay)
            {
                _isBusy   = false;
                _busyLabel = null;
                StateHasChanged();
            }
        }
    }

    /// <inheritdoc/>
    public async Task LoadRootsAsync()
    {
        _error = null;
        _roots.Clear();
        _byId.Clear();
        _expanded.Clear();   // проекция текущего дерева; наполнится заново в RestoreStateAsync

        await RunBusyAsync("Загрузка дерева…", async () =>
        {
            var result = await _dataSource.LoadLevelAsync(new ClayTreeLoadRequest(null));
            if (result.Error is not null)
            {
                _error = result.Error;
                await OnLoadError.InvokeAsync(result.Error);
            }
            else
            {
                _roots.AddRange(result.Nodes);
                IndexNodes(result.Nodes, null);
                await RestoreStateAsync();
            }
        });
    }

    /// <summary>Загружает детей узла, если они ещё не загружены.</summary>
    public async Task EnsureChildrenLoadedAsync(ClayTreeNode node)
    {
        if (node.IsLoaded) return;
        if (!node.HasChildren) return;

        node.IsLoading = true;
        StateHasChanged();

        try
        {
            await RunBusyAsync("Загрузка…", async () =>
            {
                var ds = ResolveDataSourceForNode(node, cursor: null);
                var result = await ds.LoadLevelAsync(new ClayTreeLoadRequest(node));
                if (result.Error is not null)
                {
                    _error = result.Error;
                    await OnLoadError.InvokeAsync(result.Error);
                }
                else
                {
                    node.Children.Clear();
                    node.Children.AddRange(result.Nodes);
                    IndexNodes(result.Nodes, node);
                    node.IsLoaded = true;
                    node.LoadedAllChildren = !result.HasMore;
                    node.LastChildCursor = result.NextCursor;
                }
            });
        }
        finally
        {
            node.IsLoading = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Подгружает следующую порцию детей узла. No-op если уровень дочитан или
    /// пагинация не настроена.
    /// </summary>
    public async Task LoadMoreChildrenAsync(ClayTreeNode node)
    {
        if (node.LoadedAllChildren) return;
        if (node.IsLoading) return;

        node.IsLoading = true;
        StateHasChanged();

        try
        {
            await RunBusyAsync("Загрузка…", async () =>
            {
                var ds = ResolveDataSourceForNode(node, cursor: node.LastChildCursor);
                var result = await ds.LoadLevelAsync(new ClayTreeLoadRequest(node));
                if (result.Error is not null)
                {
                    _error = result.Error;
                    await OnLoadError.InvokeAsync(result.Error);
                }
                else
                {
                    node.Children.AddRange(result.Nodes);
                    IndexNodes(result.Nodes, node);
                    node.LoadedAllChildren = !result.HasMore;
                    node.LastChildCursor = result.NextCursor;
                }
            });
        }
        finally
        {
            node.IsLoading = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Создаёт источник данных для загрузки уровня.
    /// Если настроена пагинация и режим NestedSet — создаёт кейсет-источник с <c>PageSize</c> и <c>Cursor</c>.
    /// Иначе возвращает основной <see cref="_dataSource"/>.
    /// </summary>
    private IClayTreeDataSource ResolveDataSourceForNode(ClayTreeNode node, long? cursor)
    {
        if (Options.LevelPageSize <= 0 || Options.HierarchyMode != ClayTreeHierarchyMode.NestedSet)
            return _dataSource;

        var pageSource = new ClayTreeSource(
            Options.SelectSql,
            Options.HierarchyMode,
            Options.Schema,
            Options.OrderBy,
            Options.RootId,
            PageSize: Options.LevelPageSize,
            Cursor: cursor);

        return new ClaySqlTreeDataSource(ResolveDb(), pageSource);
    }

    /// <summary>Раскрывает узел по строковому ключу (с ленивой загрузкой).</summary>
    public async Task ExpandNodeAsync(string id)
    {
        if (!_byId.TryGetValue(id, out var node)) return;
        if (node.IsExpanded) return;

        // TF_F: ручной разворот отфильтрованной ноды — сбросить неполный набор,
        // загрузить полный уровень, снять пометку «(отфильтровано)».
        if (node.ChildrenAreFiltered)
        {
            node.IsLoaded = false;
            node.Children.Clear();
            node.ChildrenAreFiltered = false;
            node.HasMatchChildren = false;
        }

        await EnsureChildrenLoadedAsync(node);
        node.IsExpanded = true;
        _expanded.Add(id);

        // TF_I: обновить якорь только при ручном раскрытии (не при восстановлении)
        if (!_isRestoring)
        {
            _lastExpandedId = id;
            await SaveStateAsync();
        }
        await OnNodeExpanded.InvokeAsync(node);
        StateHasChanged();
    }

    /// <summary>Сворачивает узел по строковому ключу.</summary>
    public async Task CollapseNodeAsync(string id)
    {
        if (!_byId.TryGetValue(id, out var node)) return;
        if (!node.IsExpanded) return;

        node.IsExpanded = false;
        _expanded.Remove(id);
        await SaveStateAsync();
        await OnNodeCollapsed.InvokeAsync(node);
        StateHasChanged();
    }

    /// <summary>Переключение раскрытия/сворачивания. Ключ — из узла, не пересчитывается.</summary>
    public async Task ToggleAsync(ClayTreeNode node)
    {
        if (node.IsExpanded)
            await CollapseNodeAsync(node.Id);
        else
            await ExpandNodeAsync(node.Id);
    }

    private void IndexNodes(IReadOnlyList<ClayTreeNode> nodes, ClayTreeNode? parent)
    {
        foreach (var node in nodes)
        {
            _byId[node.Id] = node;
            node.Parent = parent;
            // Рекурсивно индексируем уже загруженных детей (при восстановлении состояния)
            if (node.Children.Count > 0)
                IndexNodes(node.Children, node);
        }
    }
}
