using Clayzor.Lib.Entities.DynamicGrid;
using Clayzor.Lib.Entities.Tree;
using Clayzor.Lib.Web.Controls.Components.Tree.DataSources;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Dapper;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    /// <summary>Строковые ключи раскрытых узлов (для быстрого lookup, совместимость с IClayTreeView.ExpandedIds).</summary>
    private readonly HashSet<string> _expanded = [];

    /// <summary>Флаг: идёт программное восстановление пути (не ручное раскрытие).</summary>
    private bool _isRestoring;

    /// <inheritdoc/>
    public IReadOnlySet<string> ExpandedIds => _expanded;

    /// <summary>Сохраняет состояние дерева (якорь + выделение).</summary>
    private async Task SaveStateAsync()
    {
        if (!Options.PersistExpandedState) return;
        var state = new ClayTreeState
        {
            LastExpandedId = _lastExpandedId,
            SelectedIds    = [.._selectedIds],
        };
        await StateStore.SaveAsync(Options.TreeId, state);
    }

    /// <summary>
    /// Восстанавливает ОДИН путь от корня до целевой ноды.
    /// Ведущий ориентир — выделенная нода; если нет — якорь LastExpandedId.
    /// Если активен фильтр — восстановление не выполняется (набор уже построен).
    /// </summary>
    private async Task RestoreStateAsync()
    {
        if (!Options.PersistExpandedState) return;
        if (IsFilterActive) return; // фильтр важнее состояния

        var state = await StateStore.LoadAsync(Options.TreeId);
        if (state is null)
        {
            if (Options.InitialExpandLevel > 0)
                await ExpandToLevel(_roots, 0, Options.InitialExpandLevel);
            return;
        }

        // Ведущий ориентир: выделенная нода, иначе якорь
        var targetId = state.SelectedIds.Count > 0
            ? state.SelectedIds.First()
            : state.LastExpandedId;

        if (string.IsNullOrEmpty(targetId))
            return;

        // Получить цепочку предков целевой ноды
        var chain = await LoadAncestorChainAsync(targetId);
        if (chain.Count == 0)
            return; // ноды больше нет в данных

        _isRestoring = true;
        try
        {
            // Последовательное раскрытие сверху вниз (MARS выключен)
            foreach (var ancestorId in chain)
            {
                if (!_byId.TryGetValue(ancestorId, out var node)) continue;
                await EnsureChildrenLoadedAsync(node);
                node.IsExpanded = true;
                _expanded.Add(ancestorId);
            }

            // Восстановить выделение
            if (state.SelectedIds.Count > 0)
            {
                foreach (var id in state.SelectedIds)
                    _selectedIds.Add(id);
            }
        }
        finally
        {
            _isRestoring = false;
        }

        StateHasChanged();
    }

    /// <summary>
    /// Загружает цепочку предков узла от корня до targetId.
    /// NestedSet — диапазоном, ParentKey — CTE вверх. Возвращает Id от корня к цели.
    /// </summary>
    private async Task<List<string>> LoadAncestorChainAsync(string targetId)
    {
        // Сначала загружаем целевой узел чтобы получить его ключи
        var targetNode = await ClayTreeData.LoadNodeAsync(ResolveDb(), _source!, targetId);
        if (targetNode is null)
            return [];

        if (_source!.Mode == ClayTreeHierarchyMode.NestedSet)
        {
            // Предки: узлы, чей [L] < target.[L] AND [R] > target.[R]
            var sql = $"SELECT s.[{_source.Schema.IdColumn}] FROM ({_source.SelectSql}) s WHERE s.[{_source.Schema.LeftColumn}] < @l AND s.[{_source.Schema.RightColumn}] > @r ORDER BY s.[{_source.Schema.LeftColumn}]";
            var dp = new Dapper.DynamicParameters();
            dp.Add("l", targetNode.Left);
            dp.Add("r", targetNode.Right);
            var rows = await Clayzor.Lib.Entities.DynamicGrid.DynamicSql.QueryRowsAsync(ResolveDb(), sql, dp);
            var chain = rows.Select(r => ClaySqlTreeDataSource.ToKey(r.GetValueOrDefault(_source.Schema.IdColumn))).ToList();
            chain.Add(targetId); // сама цель в конец
            return chain;
        }
        else
        {
            // ParentKey: рекурсивный CTE вверх
            var idCol = $"[{_source.Schema.IdColumn}]";
            var parentCol = $"[{_source.Schema.ParentColumn}]";
            var sql = $"WITH Chain AS (SELECT {idCol} AS Id, {parentCol} AS Parent FROM ({_source.SelectSql}) x WHERE {idCol} = @id UNION ALL SELECT p.{idCol}, p.{parentCol} FROM ({_source.SelectSql}) p INNER JOIN Chain c ON p.{idCol} = c.Parent) SELECT Id FROM Chain OPTION (MAXRECURSION 200)";
            var dp = new Dapper.DynamicParameters();
            dp.Add("id", targetId);
            var rows = await Clayzor.Lib.Entities.DynamicGrid.DynamicSql.QueryRowsAsync(ResolveDb(), sql, dp);
            var chain = rows.Select(r => ClaySqlTreeDataSource.ToKey(r.GetValueOrDefault("Id"))).ToList();
            chain.Reverse(); // от корня к цели
            return chain;
        }
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
