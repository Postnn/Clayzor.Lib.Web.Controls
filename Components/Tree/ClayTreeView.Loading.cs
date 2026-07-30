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
                var result = await _dataSource.LoadLevelAsync(new ClayTreeLoadRequest(node));
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
                }
            });
        }
        finally
        {
            node.IsLoading = false;
            StateHasChanged();
        }
    }

    /// <summary>Раскрывает узел по строковому ключу (с ленивой загрузкой).</summary>
    public async Task ExpandNodeAsync(string id)
    {
        if (!_byId.TryGetValue(id, out var node)) return;
        if (node.IsExpanded) return;

        await EnsureChildrenLoadedAsync(node);
        node.IsExpanded = true;
        _expanded.Add(id);
        await SaveStateAsync();
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
