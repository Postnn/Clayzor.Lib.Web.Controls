using Clayzor.Lib.Entities.Tree;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Clayzor.Lib.Web.Controls.Components;
using Microsoft.Data.SqlClient;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

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

    /// <summary>Возвращает родителя узла (null для корневых узлов).</summary>
    private static ClayTreeNode? FindParentNode(ClayTreeNode node) => node.Parent;

    // ── Перезагрузка уровня одного родителя ──────────────────────────────────────

    /// <summary>
    /// Перечитывает из БД детей указанного родителя, сохраняя раскрытость уже раскрытых
    /// потомков и текущее выделение. Если <paramref name="parent"/> == null — перезагружает
    /// корневой уровень.
    /// </summary>
    internal async Task ReloadLevelAsync(ClayTreeNode? parent)
    {
        // CTFR2.2: childId → parentId (null = root) + paging boundary на parent.
        var previouslyExpanded = new Dictionary<string, string?>();
        var pagingBoundary = new Dictionary<string, int>();

        if (parent is not null)
        {
            pagingBoundary[parent.Id] = parent.Children.Count;
            foreach (var ch in parent.Children)
                if (ch.IsExpanded)
                {
                    previouslyExpanded[ch.Id] = parent.Id;
                    CollectExpandedSnapshot(ch, previouslyExpanded, pagingBoundary);
                }
        }
        else
        {
            foreach (var root in _roots)
                if (root.IsExpanded)
                {
                    previouslyExpanded[root.Id] = null; // root marker (CTFR2.2)
                    CollectExpandedSnapshot(root, previouslyExpanded, pagingBoundary);
                }
        }

        if (parent is null)
        {
            await LoadRootsAsync();

            foreach (var root in _roots)
            {
                if (previouslyExpanded.ContainsKey(root.Id) && root.HasChildren)
                {
                    root.IsExpanded = true;
                    _expanded.Add(root.Id);
                    await EnsureChildrenLoadedAsync(root);
                }
                await RestoreExpandedAsync(root, previouslyExpanded, pagingBoundary);
            }
        }
        else
        {
            foreach (var ch in parent.Children)
                RemoveFromIndex(ch);
            parent.IsLoaded = false;
            parent.Children.Clear();
            parent.LoadedAllChildren = true;
            parent.LastChildCursor = null;
            await EnsureChildrenLoadedAsync(parent);
            parent.HasChildren = parent.Children.Count > 0;

            await RestoreExpandedAsync(parent, previouslyExpanded, pagingBoundary);
        }

        StateHasChanged();
    }

    /// <summary>
    /// Собирает childId→parentId и paging boundary рекурсивно (CTFR2.3).
    /// parentId = null означает корень.
    /// </summary>
    internal static void CollectExpandedSnapshot(ClayTreeNode parentNode,
        Dictionary<string, string?> snapshot, Dictionary<string, int> pagingBoundary)
    {
        pagingBoundary[parentNode.Id] = parentNode.Children.Count;

        foreach (var child in parentNode.Children)
        {
            if (child.IsExpanded)
            {
                snapshot[child.Id] = parentNode.Id;
                CollectExpandedSnapshot(child, snapshot, pagingBoundary);
            }
        }
    }

    /// <summary>
    /// Восстанавливает раскрытость сверху вниз с bounded paging (CTFR2.2).
    /// Догружает страницы только до прежней границы (pagingBoundary) —
    /// moved/deleted child не вызывает полную загрузку уровня.
    /// </summary>
    private async Task RestoreExpandedAsync(ClayTreeNode parent,
        Dictionary<string, string?> snapshot, Dictionary<string, int> pagingBoundary)
    {
        // Проход 1: уже загруженные дети (страница 1).
        foreach (var child in parent.Children)
        {
            if (snapshot.ContainsKey(child.Id) && child.HasChildren)
            {
                child.IsExpanded = true;
                _expanded.Add(child.Id);
                await EnsureChildrenLoadedAsync(child);
                await RestoreExpandedAsync(child, snapshot, pagingBoundary);
            }
        }

        // Проход 2: bounded paging до прежней границы (CTFR2.2).
        if (parent.LoadedAllChildren || !parent.HasChildren) return;

        var neededIds = snapshot.Where(kvp => kvp.Value == parent.Id)
                                .Select(kvp => kvp.Key).ToHashSet();
        var missing = new HashSet<string>(neededIds.Where(id => !parent.Children.Any(c => c.Id == id)));
        if (missing.Count == 0) return;

        var maxChildren = pagingBoundary.GetValueOrDefault(parent.Id, 0);

        while (missing.Count > 0 && !parent.LoadedAllChildren && parent.Children.Count < maxChildren)
        {
            await LoadMoreChildrenAsync(parent);
            missing.RemoveWhere(id => parent.Children.Any(c => c.Id == id));

            foreach (var child in parent.Children)
            {
                if (snapshot.ContainsKey(child.Id) && child.HasChildren && !child.IsExpanded)
                {
                    child.IsExpanded = true;
                    _expanded.Add(child.Id);
                    await EnsureChildrenLoadedAsync(child);
                    await RestoreExpandedAsync(child, snapshot, pagingBoundary);
                }
            }
        }
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

    // ── Операции контекстного меню ───────────────────────────────────────────────

    /// <summary>Открывает диалог редактирования названия узла.</summary>
    private async Task EditNodeAsync(ClayTreeNode node)
    {
        if (string.IsNullOrEmpty(Options.EditColumn))
            throw new InvalidOperationException("ClayTreeOptions.EditColumn не задан — редактирование невозможно.");

        // Выделить ноду перед диалогом.
        await HandleNodeClick(node);

        string? path = null;
        await RunBusyAsync("Загрузка…", async () =>
        {
            path = await BuildPathAsync(node.RawId);
        });

        var parameters = new DialogParameters
        {
            ["FieldLabel"]   = "Название",
            ["InitialValue"] = node.Text,
            ["Path"]         = path,
            ["OnRefresh"]    = (Func<Task<(string?, string?)>>)(async () =>
            {
                var freshText = await ReadNodeTextAsync(node);
                var freshPath = await BuildPathAsync(node.RawId);
                return (freshText, freshPath);
            }),
        };

        var options = new DialogOptionsEx { DragMode = MudDialogDragMode.Simple };
        var dlg = await DialogService.ShowExAsync<ClayTreeNodeEditDialog>("Редактирование", parameters, options);
        var result = await dlg.Result;
        if (result is null || result.Canceled) return;

        var newValue = (string?)result.Data ?? "";
        await RunBusyAsync("Сохранение…", async () =>
        {
            try { await Mutations.UpdateNodeAsync(node.RawId!, Options.EditColumn, newValue); }
            catch (SqlException) { return; /* ошибка сохранена ISqlErrorHandler */ }
            await RefreshNodeTextAsync(node);
        });
    }

    /// <summary>Открывает диалог добавления дочернего узла.</summary>
    private async Task AddChildAsync(ClayTreeNode parent)
    {
        if (string.IsNullOrEmpty(Options.EditColumn))
            throw new InvalidOperationException("ClayTreeOptions.EditColumn не задан — добавление невозможно.");

        // Выделить родителя перед диалогом.
        await HandleNodeClick(parent);

        string? path = null;
        await RunBusyAsync("Загрузка…", async () =>
        {
            path = await BuildPathAsync(parent.RawId);
        });

        var parameters = new DialogParameters
        {
            ["FieldLabel"]   = "Название",
            ["InitialValue"] = "",
            ["Path"]         = path,
            ["OnRefresh"]    = (Func<Task<(string?, string?)>>)(async () =>
            {
                var freshPath = await BuildPathAsync(parent.RawId);
                return (null, freshPath);
            }),
        };

        var options = new DialogOptionsEx { DragMode = MudDialogDragMode.Simple };
        var dlg = await DialogService.ShowExAsync<ClayTreeNodeEditDialog>("Добавление узла", parameters, options);
        var result = await dlg.Result;
        if (result is null || result.Canceled) return;

        var value = (string?)result.Data ?? "";
        await RunBusyAsync("Добавление…", async () =>
        {
            try { await Mutations.AddChildAsync(parent.RawId, Options.EditColumn!, value); }
            catch (SqlException) { return; /* ошибка сохранена ISqlErrorHandler */ }

            parent.HasChildren = true;
            if (!parent.IsExpanded)
            {
                parent.IsExpanded = true;
                _expanded.Add(parent.Id);
            }
            await ReloadLevelAsync(parent);
        });
    }

    /// <summary>Удаляет узел после подтверждения.</summary>
    private async Task DeleteNodeAsync(ClayTreeNode node)
    {
        var confirmed = await ConfirmAsync($"Вы уверены, что хотите удалить {node.Text}?");
        if (!confirmed) return;

        var parent = FindParentNode(node);
        await RunBusyAsync("Удаление…", async () =>
        {
            try { await Mutations.DeleteAsync(node.RawId!); }
            catch (SqlException) { return; /* ошибка сохранена ISqlErrorHandler */ }
            _selectedIds.Remove(node.Id);
            await ReloadLevelAsync(parent);
        });
    }

    /// <summary>Выполняет кастомную операцию меню.</summary>
    private static async Task ExecuteCustomAsync(ClayTreeMenuItem item, ClayTreeNode node)
        => await item.OnExecute(node);

    /// <summary>
    /// Строит полный путь к узлу через сервис мутаций и настроенную SQL-функцию.
    /// Возвращает null, если RawId == null или NodePathFunction не задана.
    /// </summary>
    private async Task<string?> BuildPathAsync(object? rawId)
    {
        if (rawId is null || string.IsNullOrEmpty(Options.NodePathFunction))
            return null;
        try { return await Mutations.GetNodePathAsync(rawId, Options.NodePathFunction!, Options.NodePathDirection); }
        catch (SqlException) { return null; /* путь необязателен */ }
    }

    /// <summary>
    /// Диалог подтверждения действия. Возвращает true, если пользователь нажал «Да».
    /// </summary>
    private async Task<bool> ConfirmAsync(string message)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Message, message }
        };
        var options = new DialogOptionsEx { DragMode = MudDialogDragMode.Simple };
        var dialog = await DialogService.ShowExAsync<ConfirmDialog>("Подтверждение", parameters, options);
        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }

    // ── Публичные обёртки для ClayTreeNodeView ──────────────────────────────────

    internal Task InvokeEditAsync(ClayTreeNode node)      => EditNodeAsync(node);
    internal Task InvokeAddChildAsync(ClayTreeNode node)  => AddChildAsync(node);
    internal Task InvokeDeleteAsync(ClayTreeNode node)    => DeleteNodeAsync(node);
    internal Task InvokeCustomAsync(ClayTreeMenuItem item, ClayTreeNode node) => ExecuteCustomAsync(item, node);
}
