using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    /// <summary>Строковые ключи раскрытых узлов.</summary>
    private readonly HashSet<string> _expanded = [];

    /// <inheritdoc/>
    public IReadOnlySet<string> ExpandedIds => _expanded;

    /// <summary>Сохраняет состояние дерева.</summary>
    private async Task SaveStateAsync()
    {
        if (!Options.PersistExpandedState) return;
        var state = new Models.ClayTreeState { ExpandedIds = [.._expanded] };
        await StateStore.SaveAsync(Options.TreeId, state);
    }

    /// <summary>Восстанавливает раскрытое состояние из хранилища.</summary>
    private async Task RestoreStateAsync()
    {
        if (!Options.PersistExpandedState) return;

        var state = await StateStore.LoadAsync(Options.TreeId);
        if (state is null || state.ExpandedIds.Count == 0)
        {
            // Нет сохранённого состояния — применяем InitialExpandLevel
            if (Options.InitialExpandLevel > 0)
                await ExpandToLevel(_roots, 0, Options.InitialExpandLevel);
            return;
        }

        // Восстановление: последовательный обход сверху вниз (MARS выключен, параллелить нельзя)
        var toExpand = new Queue<string>(state.ExpandedIds.Where(id => _byId.ContainsKey(id)));
        var missing = state.ExpandedIds.Where(id => !_byId.ContainsKey(id)).ToList();

        // Вычищаем отсутствующие в данных ключи
        if (missing.Count > 0)
        {
            foreach (var id in missing) state.ExpandedIds.Remove(id);
            await SaveStateAsync();
        }

        while (toExpand.Count > 0)
        {
            var id = toExpand.Dequeue();
            if (!_byId.TryGetValue(id, out var node)) continue;

            await EnsureChildrenLoadedAsync(node);
            node.IsExpanded = true;
            _expanded.Add(id);

            // Добавляем в очередь детей этого узла, которые есть в сохранённом состоянии
            foreach (var child in node.Children)
            {
                if (state.ExpandedIds.Contains(child.Id))
                    toExpand.Enqueue(child.Id);
            }
        }

        StateHasChanged();
    }

    /// <summary>Раскрывает узлы до заданного уровня (0 — корни уже загружены).</summary>
    private async Task ExpandToLevel(List<ClayTreeNode> nodes, int currentLevel, int targetLevel)
    {
        if (currentLevel >= targetLevel) return;

        foreach (var node in nodes)
        {
            if (!node.HasChildren) continue;
            await EnsureChildrenLoadedAsync(node);
            node.IsExpanded = true;
            _expanded.Add(node.Id);
            if (node.Children.Count > 0)
                await ExpandToLevel(node.Children, currentLevel + 1, targetLevel);
        }
    }
}
