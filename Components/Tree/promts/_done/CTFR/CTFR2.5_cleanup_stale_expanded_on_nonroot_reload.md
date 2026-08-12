# CTFR2.5 — Cleanup stale `_expanded` on non-root reload

## Scope
Исправить только stale `_expanded` при `ReloadLevelAsync(parent)`. Paging logic CTFR2.3 и bUnit tests CTFR2.4 не переписывать.

## Defect
Root reload очищает `_expanded`, а non-root reload удаляет старые descendants из `_byId`/`Children`, но исчезнувшие или moved nodes могут остаться в `_expanded`.

Пример:
```text
до: _expanded = { A, X }
A -> X expanded
X удалён/reparented
ReloadLevelAsync(A)

после должно быть:
_expanded = { A }
```

## Required invariant
После reload каждый Id в `_expanded` соответствует реально существующему и восстановленному expanded node текущего дерева.

Правильный порядок:
```text
1 CollectExpandedSnapshot + recursive pagingBoundary
2 cleanup `_expanded` только старого заменяемого subtree
3 remove old subtree from `_byId`
4 clear/load children
5 RestoreExpandedAsync
6 restore добавляет обратно только реально найденные nodes
```

Snapshot ОБЯЗАТЕЛЬНО собирать до cleanup.

## Cleanup scope
НЕ делать `_expanded.Clear()` при non-root reload.
Не удалять сам `parent` из `_expanded`, если заменяются только его children.
Не затрагивать sibling branches.

Сначала найти ВСЕ call sites `RemoveFromIndex`.

Если `RemoveFromIndex` всегда означает окончательное удаление node/subtree из текущего дерева, допустимо:
```csharp
private void RemoveFromIndex(ClayTreeNode node)
{
    _byId.Remove(node.Id);
    _expanded.Remove(node.Id);
    foreach (var child in node.Children)
        RemoveFromIndex(child);
}
```

Если у helper есть другая семантика — НЕ менять глобально. Сделать отдельный recursive helper:
```csharp
private void RemoveExpandedFromSubtree(ClayTreeNode node)
{
    _expanded.Remove(node.Id);
    foreach (var child in node.Children)
        RemoveExpandedFromSubtree(child);
}
```

Не сбрасывать `IsExpanded` старых instances.

## Moved/deleted semantics
Moved X из A в B после `ReloadLevelAsync(A)`:
```text
A subtree не содержит X
_expanded не содержит X
```
Не искать X глобально под B.

Deleted X:
```text
X отсутствует в A.Children
X отсутствует в _byId
X отсутствует в _expanded
```

## Unaffected branch
Для:
```text
Root
├─ A expanded
│  └─ X expanded
└─ B expanded
   └─ Y expanded
```
после удаления X и `ReloadLevelAsync(A)`:
```text
A есть в _expanded
X нет
B есть
Y есть
B.IsExpanded == true
Y.IsExpanded == true
```

## Surviving descendants
Если X/Z существуют и после reload:
```text
A
├─ X expanded
└─ Z expanded
```
то после cleanup + real Restore:
```text
A,X,Z присутствуют в _expanded
X.IsExpanded == true
Z.IsExpanded == true
```

# Tests — только настоящий bUnit production path

Не копировать restore algorithm вручную.

### 1. Усилить moved test
`Reload_MovedExpandedChild_StopsAtPreviousBoundary`

Добавить:
```csharp
Assert.DoesNotContain("X", GetExpanded(view));
```
Сохранить bounded paging assertion. Если fake deterministic при boundary=4/PageSize=2 — предпочесть точный `Assert.Equal(2, calls)`.

### 2. Усилить deleted test
`Reload_DeletedExpandedChild_StopsAtPreviousBoundary`

Проверить:
- X отсутствует в Children;
- X отсутствует в `_expanded`;
- X отсутствует в `_byId`, если это легко проверить без public API;
- full scan отсутствует.

### 3. Новый test
`ReloadNonRoot_CleansOnlyReloadedSubtreeExpandedIds`

Подготовить A/X и B/Y как выше, удалить X, вызвать настоящий:
```csharp
await cut.InvokeAsync(() => view.ReloadLevelAsync(aNode));
```
Проверить A/B/Y остаются в `_expanded`, X удалён. B/Y не пересозданы и не сброшены.

### 4. Новый test
`ReloadNonRoot_SurvivingExpandedDescendants_AreRestoredIntoExpandedSet`

X/Z существуют после reload. Проверить, что production Restore добавил их обратно в `_expanded` и выставил `IsExpanded`.

### 5. Root regression
`ReloadRoot_DeepExpandedChildOnPage2_RestoresViaProductionPaging` остаётся зелёным.

Все CTFR2.4 tests должны остаться зелёными:
```text
ReloadRoot_DeepExpandedChildOnPage2_RestoresViaProductionPaging
ReloadNonRoot_DeepExpandedChildOnPage2_RestoresViaProductionPaging
Reload_MovedExpandedChild_StopsAtPreviousBoundary
Reload_DeletedExpandedChild_StopsAtPreviousBoundary
Reload_AfterRestore_LoadMoreContinuesCorrectly
Reload_MultipleExpandedChildren_LoadsEachPageOnce
Reload_PagingDisabled_PreservesExpandedState
```

## Запрещено менять
- paging boundary/cursor/LoadMore semantics;
- ClayTreeState/SaveStateAsync/LastExpandedId/selection;
- datasource contracts;
- DnD/SQL;
- CTFR3.x;
- bUnit renderer/test seam, кроме необходимых test assertions/helpers.

## AGENTS.md/docs
По существующему правилу проекта обновить связанные AGENTS.md/docs. Зафиксировать инвариант: non-root reload сохраняет snapshot, очищает runtime expanded state только старого заменяемого subtree и восстанавливает `_expanded` только для реально найденных nodes; ветки вне reload scope не затрагиваются.

## Self-check
1. `{A,X}` + deleted X + reload A => `{A}`.
2. `{A,X,B,Y}` + deleted X + reload A => `{A,B,Y}`.
3. Surviving X после reload снова попадает в `_expanded` через production Restore.
4. Все CTFR2.4 paging tests зелёные.

## Acceptance report
Указать:
1. production-файл и изменение;
2. все найденные call sites `RemoveFromIndex`;
3. почему выбран global cleanup или отдельный helper;
4. порядок `snapshot -> cleanup -> reload -> restore`;
5. moved/deleted `_expanded` assertions;
6. unaffected sibling test;
7. surviving descendant test;
8. parent A остаётся expanded;
9. root regression;
10. paging algorithm не менялся;
11. build result;
12. full test suite result.

Главный acceptance criterion:
```text
исчезнувший из reload scope node не остаётся stale Id в _expanded,
а expanded state вне reload scope сохраняется.
```
