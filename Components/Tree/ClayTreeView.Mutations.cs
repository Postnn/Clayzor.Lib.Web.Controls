using Clayzor.Lib.Entities.Tree;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    // ── Поиск узлов в загруженном дереве ─────────────────────────────────────────

    /// <summary>
    /// Ищет узел по строковому Id в загруженном дереве (обход в глубину от корней).
    /// Возвращает null, если узел не загружен в память.
    /// </summary>
    private ClayTreeNode? FindNodeById(string id)
    {
        foreach (var root in _roots)
        {
            var found = FindInSubtree(root, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static ClayTreeNode? FindInSubtree(ClayTreeNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            var found = FindInSubtree(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    // ── Перезагрузка уровня одного родителя ──────────────────────────────────────

    /// <summary>
    /// Перечитывает из БД детей указанного родителя, сохраняя раскрытость уже раскрытых
    /// потомков и текущее выделение. Если <paramref name="parent"/> == null — перезагружает
    /// корневой уровень.
    /// </summary>
    private async Task ReloadLevelAsync(ClayTreeNode? parent)
    {
        // Запомнить, какие дети были раскрыты (по Id), чтобы восстановить после перезагрузки.
        var previouslyExpanded = new HashSet<string>();
        var childrenBefore = parent?.Children ?? _roots;
        foreach (var ch in childrenBefore)
            if (ch.IsExpanded) previouslyExpanded.Add(ch.Id);

        if (parent is null)
        {
            // Корневой уровень. LoadRootsAsync сбрасывает _expanded — сохраняем все раскрытые.
            var allExpanded = new HashSet<string>(_expanded);

            await LoadRootsAsync();

            // Восстановить раскрытость для узлов, оставшихся в дереве.
            foreach (var id in allExpanded)
            {
                if (_byId.TryGetValue(id, out var node) && node.HasChildren)
                {
                    node.IsExpanded = true;
                    _expanded.Add(id);
                    await EnsureChildrenLoadedAsync(node);
                }
            }
        }
        else
        {
            // Сбросить загрузку уровня и перезагрузить лениво.
            parent.IsLoaded = false;
            parent.Children.Clear();
            parent.LoadedAllChildren = true;
            parent.LastChildCursor = null;
            await EnsureChildrenLoadedAsync(parent);

            // Восстановить раскрытость тех детей, что снова присутствуют и были раскрыты.
            foreach (var ch in parent.Children)
            {
                if (previouslyExpanded.Contains(ch.Id) && ch.HasChildren)
                {
                    ch.IsExpanded = true;
                    _expanded.Add(ch.Id);
                    await EnsureChildrenLoadedAsync(ch);
                }
            }
        }

        StateHasChanged();
    }

    // ── Перечитывание текста одного узла ─────────────────────────────────────────

    /// <summary>
    /// Читает актуальный текст узла (TextColumn) из БД по его RawId.
    /// Возвращает null, если строка не найдена.
    /// </summary>
    private async Task<string?> ReadNodeTextAsync(ClayTreeNode node)
    {
        if (node.RawId is null) return null;
        var row = await ClayTreeData.LoadNodeAsync(ResolveDb(), _source!, node.RawId);
        return row?.Text;
    }

    /// <summary>
    /// Перечитывает из БД текст ОДНОГО узла (после редактирования поля названия).
    /// </summary>
    private async Task RefreshNodeTextAsync(ClayTreeNode node)
    {
        var newText = await ReadNodeTextAsync(node);
        if (newText is not null)
        {
            node.Text = newText;
            StateHasChanged();
        }
    }
}
