using Clayzor.Lib.Entities.Tree;
using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Перетаскиваемый узел (null — нет активного перетаскивания).</summary>
    private ClayTreeNode? _dragNode;

    /// <summary>Узел-цель под курсором и зона ('before'/'on'/'after').</summary>
    private ClayTreeNode? _dropTarget;
    private string _dropZone = "on";

    /// <summary>Допустим ли текущий дроп (для стиля индикатора).</summary>
    private bool _dropAllowed;

    internal bool IsDragging => _dragNode is not null;

    internal void OnDragStart(ClayTreeNode node)
    {
        if (!Options.EnableDragDrop) return;
        _dragNode = node;
    }

    internal void OnDragEnd()
    {
        _dragNode = null;
        _dropTarget = null;
        _dropAllowed = false;
        StateHasChanged();
    }

    /// <summary>
    /// Вызывается из dragover строки: определяет зону (JS) и допустимость дропа,
    /// обновляет индикатор.
    /// </summary>
    internal async Task OnDragOverAsync(ClayTreeNode target, double clientY, Microsoft.AspNetCore.Components.ElementReference rowRef)
    {
        if (_dragNode is null) return;

        try
        {
            _dropZone   = await JS.InvokeAsync<string>("clayTreeDnd.zone", rowRef, clientY);
            _dropTarget = target;
            _dropAllowed = await IsDropAllowedAsync(_dragNode, target, _dropZone);
            StateHasChanged();
        }
        catch (JSDisconnectedException) { /* circuit уже закрыт */ }
        catch (ObjectDisposedException) { /* JS-рантайм освобождён */ }
        catch (InvalidOperationException) { /* prerendering / нет JS */ }
    }

    /// <summary>Выполняет дроп: reorder или reparent, с подтверждением и обновлением.</summary>
    internal async Task OnDropAsync(ClayTreeNode target)
    {
        var dragged = _dragNode;
        var zone    = _dropZone;
        OnDragEnd();   // сбросить индикатор до диалогов

        if (dragged is null || target is null) return;
        if (ReferenceEquals(dragged, target)) return;
        if (!await IsDropAllowedAsync(dragged, target, zone)) return;

        if (zone == "on")
        {
            await DoReparentAsync(dragged, target);
        }
        else
        {
            // 'before'/'after' — вставка среди детей родителя цели.
            var dropParent = target.Parent;
            var sameParent = NodesShareParent(dragged, target);

            if (sameParent && Options.HierarchyMode == ClayTreeHierarchyMode.NestedSet)
            {
                await DoReorderAsync(dragged, target, zone);
            }
            else if (!sameParent)
            {
                // перенос в другой уровень = переподчинение родителю зоны дропа
                await DoReparentAsync(dragged, dropParent);
            }
            // sameParent && ParentKey => no-op (порядок не хранится)
        }
    }

    // ── Допустимость ─────────────────────────────────────────────────────────────

    private async Task<bool> IsDropAllowedAsync(ClayTreeNode dragged, ClayTreeNode target, string zone)
    {
        if (ReferenceEquals(dragged, target)) return false;

        // Нельзя переподчинить узел его собственному потомку (или самому себе).
        var effectiveParent = zone == "on" ? target : target.Parent;
        if (effectiveParent is not null)
        {
            if (ReferenceEquals(effectiveParent, dragged)) return false;
            if (await IsDescendantOfAsync(effectiveParent, dragged)) return false;
        }

        // Reorder в ParentKey бессмысленен.
        if (zone != "on" && NodesShareParent(dragged, target)
            && Options.HierarchyMode == ClayTreeHierarchyMode.ParentKey)
            return false;

        return true;
    }

    /// <summary>candidate является потомком ancestor?</summary>
    private async Task<bool> IsDescendantOfAsync(ClayTreeNode candidate, ClayTreeNode ancestor)
    {
        if (Options is null) return false;

        if (Options.HierarchyMode == ClayTreeHierarchyMode.NestedSet
            && candidate.Left.HasValue && candidate.Right.HasValue
            && ancestor.Left.HasValue && ancestor.Right.HasValue)
        {
            // candidate внутри поддерева ancestor
            return candidate.Left > ancestor.Left && candidate.Right < ancestor.Right;
        }

        // ParentKey (или нет L/R) — спрашиваем БД.
        if (candidate.RawId is null || ancestor.RawId is null) return false;
        return await Mutations.IsDescendantAsync(candidate.RawId, ancestor.RawId);
    }

    private static bool NodesShareParent(ClayTreeNode a, ClayTreeNode b)
        => ReferenceEquals(a.Parent, b.Parent);

    /// <summary>
    /// True, если перетаскиваемый узел уже находится в требуемой позиции относительно цели.
    /// No-op: узел уже непосредственно перед/после target — перемещение не меняет порядок.
    /// </summary>
    private static bool IsReorderNoOp(
        IReadOnlyList<ClayTreeNode> siblings,
        ClayTreeNode dragged,
        ClayTreeNode target,
        string zone)
    {
        var draggedIdx = -1;
        var targetIdx = -1;
        for (var i = 0; i < siblings.Count; i++)
        {
            if (ReferenceEquals(siblings[i], dragged)) draggedIdx = i;
            if (ReferenceEquals(siblings[i], target)) targetIdx = i;
        }
        if (draggedIdx < 0 || targetIdx < 0) return false;

        if (zone == "before" && draggedIdx + 1 == targetIdx) return true;
        if (zone == "after" && draggedIdx == targetIdx + 1) return true;
        return false;
    }

    // ── Операции ───────────────────────────────────────────────────────────────

    private async Task DoReparentAsync(ClayTreeNode dragged, ClayTreeNode? newParent)
    {
        var draggedName = dragged.Text;
        var parentName  = newParent?.Text ?? "корень";
        var confirmed = await ConfirmAsync(
            $"Вы уверены, что хотите сделать {draggedName} дочерним по отношению к {parentName}?");
        if (!confirmed) return;

        // Сохранить стабильные идентификаторы до любых reload.
        var oldParent  = dragged.Parent;
        var oldParentId = oldParent?.Id;
        var newParentId = newParent?.Id;
        var draggedId  = dragged.Id;
        var sameParent = oldParentId == newParentId;

        try
        {
            await RunBusyAsync("Перемещение…", async () =>
            {
                await Mutations.ReparentAsync(dragged.RawId!, newParent?.RawId);

                await ReloadLevelAsync(oldParent);

                if (!sameParent)
                {
                    // newParent мог быть уничтожен первой перезагрузкой — найти актуальный экземпляр.
                    var freshNewParent = newParentId is null ? null : FindNodeById(newParentId);
                    if (freshNewParent is not null || newParentId is null)
                        await ReloadLevelAsync(freshNewParent);
                }

                RestoreFocus(draggedId);
            });
        }
        catch (JSDisconnectedException) { /* circuit уже закрыт */ }
        catch (ObjectDisposedException) { /* JS-рантайм освобождён */ }
        catch (InvalidOperationException) { /* prerendering / нет JS */ }
        catch (Exception) { /* ошибка уже сохранена ISqlErrorHandler */ }
    }

    private async Task DoReorderAsync(ClayTreeNode dragged, ClayTreeNode target, string zone)
    {
        var siblings = dragged.Parent?.Children ?? _roots;

        // No-op: узел уже в требуемой позиции.
        if (IsReorderNoOp(siblings, dragged, target, zone))
            return;

        var message = dragged.Parent is null
            ? "Вы уверены, что хотите изменить порядок в рамках корневого уровня?"
            : $"Вы уверены, что хотите изменить порядок в рамках одной группы {dragged.Parent.Text}?";
        var confirmed = await ConfirmAsync(message);
        if (!confirmed) return;

        try
        {
            await RunBusyAsync("Перемещение…", async () =>
            {
                long newL = ComputeNewLeft(siblings, dragged, target, zone);

                await Mutations.ReorderAsync(dragged.RawId!, dragged.Parent?.RawId, newL);

                await ReloadLevelAsync(dragged.Parent);
                RestoreFocus(dragged.Id);
            });
        }
        catch (JSDisconnectedException) { /* circuit уже закрыт */ }
        catch (ObjectDisposedException) { /* JS-рантайм освобождён */ }
        catch (InvalidOperationException) { /* prerendering / нет JS */ }
        catch (Exception) { /* ошибка уже сохранена ISqlErrorHandler */ }
    }

    /// <summary>
    /// Значение L для reorder: L сиблинга, после которого встаёт узел.
    /// Для позиции «в начало» — L текущего первого сиблинга.
    /// </summary>
    private static long ComputeNewLeft(IReadOnlyList<ClayTreeNode> siblings, ClayTreeNode dragged, ClayTreeNode target, string zone)
    {
        // Индекс цели среди сиблингов.
        var targetIdx = -1;
        for (var i = 0; i < siblings.Count; i++)
            if (ReferenceEquals(siblings[i], target)) { targetIdx = i; break; }
        if (targetIdx < 0) targetIdx = 0;

        // 'before' target → встаём перед target; сиблинг «после которого» — предыдущий.
        // 'after'  target → встаём после target; сиблинг «после которого» — сам target.
        ClayTreeNode? afterSibling;
        if (zone == "after")
            afterSibling = target;
        else // before
            afterSibling = targetIdx > 0 ? siblings[targetIdx - 1] : null;

        if (afterSibling is null)
        {
            // Вставка в начало: берём L текущего первого сиблинга.
            var first = siblings.Count > 0 ? siblings[0] : null;
            return first?.Left ?? 0L;
        }

        return afterSibling.Left ?? 0L;
    }

    private void RestoreFocus(string nodeId)
    {
        var node = FindNodeById(nodeId);
        if (node is not null)
        {
            _selectedIds.Clear();
            _selectedIds.Add(node.Id);
        }
        StateHasChanged();
    }

    // ── Публичные геттеры индикатора (для ClayTreeNodeView) ─────────────────────

    internal bool IsCurrentDropTarget(ClayTreeNode node) => ReferenceEquals(_dropTarget, node);
    internal bool IsCurrentDropAllowed => _dropAllowed;
    internal string CurrentDropZone => _dropZone;
}
