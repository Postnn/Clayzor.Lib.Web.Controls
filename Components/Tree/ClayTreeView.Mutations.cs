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
    private async Task ReloadLevelAsync(ClayTreeNode? parent)
    {
        // Собрать Id всех раскрытых узлов в поддереве перед перезагрузкой.
        var previouslyExpanded = new HashSet<string>();
        if (parent is not null)
        {
            foreach (var ch in parent.Children)
                if (ch.IsExpanded)
                {
                    previouslyExpanded.Add(ch.Id);
                    CollectExpandedIds(ch, previouslyExpanded);
                }
        }
        else
        {
            // Корневой уровень: собираем раскрытые Id со всего дерева рекурсивно.
            foreach (var root in _roots)
                if (root.IsExpanded)
                {
                    previouslyExpanded.Add(root.Id);
                    CollectExpandedIds(root, previouslyExpanded);
                }
        }

        if (parent is null)
        {
            // Корневой уровень. LoadRootsAsync сбрасывает _expanded и _byId,
            // восстанавливая только один anchor-путь (RestoreStateAsync).
            await LoadRootsAsync();

            // Восстановить раскрытость сверху вниз: корни, затем рекурсивно потомки.
            foreach (var root in _roots)
            {
                if (previouslyExpanded.Contains(root.Id) && root.HasChildren)
                {
                    root.IsExpanded = true;
                    _expanded.Add(root.Id);
                    await EnsureChildrenLoadedAsync(root);
                }
                await RestoreExpandedAsync(root, previouslyExpanded);
            }
        }
        else
        {
            // Сбросить загрузку уровня и перезагрузить лениво.
            foreach (var ch in parent.Children)
                RemoveFromIndex(ch);
            parent.IsLoaded = false;
            parent.Children.Clear();
            parent.LoadedAllChildren = true;
            parent.LastChildCursor = null;
            await EnsureChildrenLoadedAsync(parent);
            parent.HasChildren = parent.Children.Count > 0;

            // Рекурсивно восстановить раскрытость потомков на любой глубине.
            await RestoreExpandedAsync(parent, previouslyExpanded);
        }

        StateHasChanged();
    }

    /// <summary>Рекурсивно собирает Id раскрытых узлов в поддереве.</summary>
    internal static void CollectExpandedIds(ClayTreeNode node, HashSet<string> ids)
    {
        foreach (var child in node.Children)
        {
            if (child.IsExpanded)
            {
                ids.Add(child.Id);
                CollectExpandedIds(child, ids);
            }
        }
    }

    /// <summary>Рекурсивно восстанавливает раскрытость узлов по сохранённым Id.</summary>
    private async Task RestoreExpandedAsync(ClayTreeNode node, HashSet<string> ids)
    {
        foreach (var child in node.Children)
        {
            if (ids.Contains(child.Id) && child.HasChildren)
            {
                child.IsExpanded = true;
                _expanded.Add(child.Id);
                await EnsureChildrenLoadedAsync(child);
                await RestoreExpandedAsync(child, ids);
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
