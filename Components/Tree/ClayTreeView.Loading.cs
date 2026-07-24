using Clayzor.Lib.Web.Controls.Components.Tree.DataSources;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    /// <summary>Плоский индекс всех загруженных узлов по строковому ключу.</summary>
    private readonly Dictionary<string, ClayTreeNode> _byId = [];

    /// <inheritdoc/>
    public async Task LoadRootsAsync()
    {
        _initialLoading = true;
        _error = null;
        _roots.Clear();
        _byId.Clear();

        try
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
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            await OnLoadError.InvokeAsync(ex.Message);
        }
        finally
        {
            _initialLoading = false;
        }
    }

    /// <summary>Загружает детей узла, если они ещё не загружены.</summary>
    public async Task EnsureChildrenLoadedAsync(ClayTreeNode node)
    {
        if (node.IsLoaded || node.IsLoading) return;
        if (!node.HasChildren) return;

        node.IsLoading = true;
        StateHasChanged();

        try
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
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            await OnLoadError.InvokeAsync(ex.Message);
        }
        finally
        {
            node.IsLoading = false;
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

    /// <summary>Переключение раскрытия/сворачивания.</summary>
    public async Task ToggleAsync(ClayTreeNode node)
    {
        var key = ClaySqlTreeDataSource.ToKey(node.RawId);
        if (node.IsExpanded)
            await CollapseNodeAsync(key);
        else
            await ExpandNodeAsync(key);
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
