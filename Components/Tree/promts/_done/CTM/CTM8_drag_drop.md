# CTM8 — Drag-and-drop: индикатор места дропа, запрет недопустимого, подтверждения, мутации

## Цель

Реализовать перетаскивание узлов с визуальной обратной связью и записью в БД:
- **между узлами** (верхние/нижние ~25% строки) на том же уровне → **reorder** (только NestedSet);
- **на узел** (средние ~50% строки) → **reparent** (сделать перетаскиваемый ребёнком цели);
- перенос между узлами разных родителей трактуется как **reparent** к родителю зоны дропа;
- недопустимые цели (сделать предка потомком своего потомка; сам в себя) подсвечиваются как
  запрещённые, дроп на них игнорируется;
- каждое перемещение подтверждается пользователем текстом из требований;
- после изменения — перезагрузка затронутых уровней (CTM6), фокус на перетаскиваемом узле.

Режим включается `Options.EnableDragDrop`. Выполняется ПОСЛЕ CTM6 и CTM7.

## Модель поведения (зафиксировано)

- Reorder — только внутри одного и того же родителя и только в NestedSet. В ParentKey «между
  узлами» на том же уровне = no-op (порядок не хранится) → показывать как недопустимую зону
  reorder, но допускать reparent к другому родителю.
- `@NewL` для reorder = значение `L` сиблинга, ПОСЛЕ которого встаёт узел; для позиции 0 — `L`
  текущего первого сиблинга (см. CTM0/CTM1).
- Проверка «потомок»: NestedSet — по `L`/`R` в памяти; ParentKey — `IsDescendantAsync` (БД).

## Шаг 1 — JS-хелпер зоны дропа: файл `Clayzor.Lib.Web.Controls/wwwroot/js/clayTreeDnd.js` (новый)

Определяет по позиции курсора зону строки: `before` / `on` / `after`. HTML5 DnD в Blazor не отдаёт
координаты внутри строки надёжно, поэтому считаем в JS по `dragover`.

```javascript
window.clayTreeDnd = window.clayTreeDnd || {};

// Возвращает 'before' | 'on' | 'after' по вертикальной позиции курсора внутри строки.
window.clayTreeDnd.zone = function (rowEl, clientY) {
    if (!rowEl) return 'on';
    const rect = rowEl.getBoundingClientRect();
    const y = clientY - rect.top;
    const h = rect.height;
    if (y < h * 0.25) return 'before';
    if (y > h * 0.75) return 'after';
    return 'on';
};
```

Подключить файл тем же способом, что и прочие скрипты дерева (по аналогии с `clayTreePaging.js`).

## Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeDragDrop.cs` (новый partial)

Состояние перетаскивания и логика. Поля — ЭКЗЕМПЛЯРА (не static): в Blazor Server статика течёт
между пользователями.

```csharp
using Clayzor.Lib.Web.Controls.Components.Tree.Models;
using Microsoft.AspNetCore.Components.Web;

namespace Clayzor.Lib.Web.Controls.Components.Tree;

public partial class ClayTreeView
{
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
    /// обновляет индикатор. Возвращает признак допустимости (для DropEffect).
    /// </summary>
    internal async Task OnDragOverAsync(ClayTreeNode target, double clientY, Microsoft.AspNetCore.Components.ElementReference rowRef)
    {
        if (_dragNode is null) return;

        _dropZone   = await JS.InvokeAsync<string>("clayTreeDnd.zone", rowRef, clientY);
        _dropTarget = target;
        _dropAllowed = await IsDropAllowedAsync(_dragNode, target, _dropZone);
        StateHasChanged();
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
            var dropParent = target.Parent;   // null => корневой уровень
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

    // ── Операции ───────────────────────────────────────────────────────────────

    private async Task DoReparentAsync(ClayTreeNode dragged, ClayTreeNode? newParent)
    {
        var draggedName = dragged.Text;
        var parentName  = newParent?.Text ?? "корень";
        var confirmed = await ConfirmAsync(
            $"Вы уверены, что хотите сделать {draggedName} дочерним по отношению к {parentName}?");
        if (!confirmed) return;

        var oldParent = dragged.Parent;
        await Mutations.ReparentAsync(dragged.RawId!, newParent?.RawId);

        // Перезагрузить старый и новый уровни, сохранить фокус на перемещённом.
        await ReloadLevelAsync(oldParent);
        if (!ReferenceEquals(oldParent, newParent))
            await ReloadLevelAsync(newParent);

        RestoreFocus(dragged.Id);
    }

    private async Task DoReorderAsync(ClayTreeNode dragged, ClayTreeNode target, string zone)
    {
        var groupName = dragged.Parent?.Text ?? "корневой группы";
        var confirmed = await ConfirmAsync(
            $"Вы уверены, что хотите изменить порядок в рамках одной группы {groupName}?");
        if (!confirmed) return;

        // Определить сиблинга, ПОСЛЕ которого встаёт узел, и взять его L.
        var siblings = dragged.Parent?.Children ?? _roots;
        long newL = ComputeNewLeft(siblings, dragged, target, zone);

        await Mutations.ReorderAsync(dragged.RawId!, dragged.Parent?.RawId, newL);

        await ReloadLevelAsync(dragged.Parent);
        RestoreFocus(dragged.Id);
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

        // Если "после которого" оказался сам перетаскиваемый — берём соседа за ним.
        if (ReferenceEquals(afterSibling, dragged))
        {
            var idx = targetIdx; // приблизительно; безопасно вернуть L цели
            return target.Left ?? 0L;
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
}
```

Замечания для агента:
- `ComputeNewLeft` даёт «сырой» L-маркер; окончательный порядок нормализует триггер БД. Не пытаться
  «улучшать» расчёт вводом дробных/промежуточных значений — только реальные L существующих строк.
- `ConfirmAsync` и `ReloadLevelAsync`, `FindNodeById` — из CTM6/CTM7. `Mutations` — из CTM3.
- Тексты подтверждений — ДОСЛОВНО из требований: reorder «Вы уверены, что хотите изменить порядок
  в рамках одной группы [название]?»; reparent «Вы уверены, что хотите сделать [название] дочерним
  по отношению к [название]?».

## Шаг 3 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeNodeView.razor` (дополнить)

Сделать строку узла перетаскиваемой и целью дропа. Обернуть/дополнить `.clay-tree-node-row`.

```razor
@{
    var dndOn = (Tree as ClayTreeView)?.Options.EnableDragDrop == true;
    var tv = Tree as ClayTreeView;
    var isDropTarget = tv is not null && tv.IsCurrentDropTarget(Node);
    var dropClass = !dndOn ? "" :
        isDropTarget ? (tv!.IsCurrentDropAllowed ? $" clay-tree-drop-{tv.CurrentDropZone}" : " clay-tree-drop-forbidden")
                     : "";
}

<div class="clay-tree-node-row@(dropClass)"
     @ref="_rowRef"
     draggable="@dndOn.ToString().ToLowerInvariant()"
     @ondragstart="@(() => tv!.OnDragStart(Node))"
     @ondragstart:preventDefault="false"
     @ondragend="@(() => tv!.OnDragEnd())"
     @ondragover="@(e => tv!.OnDragOverAsync(Node, e.ClientY, _rowRef))"
     @ondragover:preventDefault="true"
     @ondrop="@(() => tv!.OnDropAsync(Node))"
     @ondrop:preventDefault="true"
     style="@(linesOn ? null : $"padding-left:{Node.Level * Tree.IndentPx}px")">
    @* ...существующее содержимое строки (шеврон, текст, пометки, меню)... *@
</div>
```

Замечания:
- Если `EnableDragDrop = false`, атрибуты DnD не должны мешать (draggable="false"; обработчики
  можно оставить — они вернутся раньше по проверке `Options.EnableDragDrop`).
- `e.ClientY` берётся из `DragEventArgs`. Обработчик `OnDragOverAsync` — асинхронный; Blazor это
  допускает.

## Шаг 4 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeNodeView.razor.cs` (дополнить)

Добавить поле ссылки на строку (если ещё нет):

```csharp
private ElementReference _rowRef;
```

## Шаг 5 — публичные геттеры индикатора: `ClayTreeDragDrop.cs` (дополнить)

`ClayTreeNodeView` должен спросить у дерева, является ли узел текущей целью и какова зона:

```csharp
public partial class ClayTreeView
{
    internal bool IsCurrentDropTarget(ClayTreeNode node) => ReferenceEquals(_dropTarget, node);
    internal bool IsCurrentDropAllowed => _dropAllowed;
    internal string CurrentDropZone => _dropZone;   // 'before' | 'on' | 'after'
}
```

## Шаг 6 — стили (`clay-tree.css`)

Индикаторы места дропа и запрета:

```css
/* линия сверху/снизу — вставка между узлами */
.clay-tree-drop-before { box-shadow: inset 0 2px 0 0 var(--lh-gold, #FFAD00); }
.clay-tree-drop-after  { box-shadow: inset 0 -2px 0 0 var(--lh-gold, #FFAD00); }
/* рамка — переподчинение «на узел» */
.clay-tree-drop-on     { outline: 2px solid var(--lh-gold, #FFAD00); outline-offset: -2px; }
/* запрещённая цель */
.clay-tree-drop-forbidden { outline: 2px dashed #d32f2f; outline-offset: -2px; cursor: not-allowed; }
/* перетаскиваемый — приглушить */
.clay-tree-node-row[draggable="true"] { cursor: grab; }
```

Цвета — из палитры проекта (`--lh-gold` и т.п.); если имена иные — заменить фактическими.

## Критерии приёмки

- `EnableDragDrop = false` — перетаскивание недоступно, поведение дерева не меняется.
- Перетаскивание в верхнюю/нижнюю четверть строки того же родителя (NestedSet) → индикатор-линия,
  подтверждение reorder дословным текстом, `ReorderAsync(id, parentId, newL)` с корректным L,
  перезагрузка уровня, фокус на перемещённом.
- Перетаскивание в середину строки → индикатор-рамка, подтверждение reparent дословным текстом,
  `ReparentAsync(id, targetId)`, перезагрузка старого и нового уровней, фокус сохранён.
- Перетаскивание к другому родителю (зона before/after его ребёнка) → reparent к этому родителю.
- Попытка сделать предка ребёнком своего потомка (или узел сам в себя): цель подсвечена как
  запрещённая, дроп игнорируется. Для ParentKey проверка идёт через `IsDescendantAsync`.
- ParentKey, reorder в пределах одного родителя → зона before/after помечена запрещённой (no-op).
- Одновременная работа двух пользователей не путает перетаскиваемые узлы (состояние — в полях
  экземпляра, не static).
