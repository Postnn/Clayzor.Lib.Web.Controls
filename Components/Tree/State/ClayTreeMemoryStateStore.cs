using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree.State;

/// <summary>
/// Хранилище состояния дерева в памяти. Живёт в пределах circuit'а Blazor Server (Scoped):
/// переходы между страницами состояние сохраняют, полная перезагрузка браузера — теряет.
/// Персистенция в БД — шаг CT5.
/// </summary>
public sealed class ClayTreeMemoryStateStore : IClayTreeStateStore
{
    private readonly Dictionary<string, ClayTreeState> _states = [];

    /// <inheritdoc/>
    public Task<ClayTreeState?> LoadAsync(string treeId, CancellationToken ct = default)
    {
        _states.TryGetValue(treeId, out var state);
        return Task.FromResult(state);
    }

    /// <inheritdoc/>
    public Task SaveAsync(string treeId, ClayTreeState state, CancellationToken ct = default)
    {
        _states[treeId] = state;
        return Task.CompletedTask;
    }
}
