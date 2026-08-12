# CTFR2.1 — Paging-aware восстановление раскрытых веток после mutation reload

## Контекст

Репозиторий: `Postnn/Clayzor.Lib.Web.Controls`.

Компонент: `Components/Tree/ClayTreeView`.

CTFR2 исправил глубокое восстановление раскрытости при `ReloadLevelAsync(null)`:
- перед root reload собираются Id всех раскрытых узлов;
- после `LoadRootsAsync()` восстановление идёт сверху вниз через `RestoreExpandedAsync`.

Основной дефект исправлен.

Остался известный edge case при включённой level paging (`Options.LevelPageSize > 0`, NestedSet): если ранее раскрытый узел находился на странице 2+ среди детей своего родителя, то после reload `EnsureChildrenLoadedAsync(parent)` загружает только первую страницу, нужного child ещё нет в `parent.Children`, и `RestoreExpandedAsync` не может его восстановить.

Пример:

```text
Root expanded
 ├─ Child001
 ...
 ├─ Child050
 ├─ Child051 expanded
 │   └─ GrandChild expanded
 ...
```

При `LevelPageSize = 50` после root reload `Child051` не попадает в первую порцию и ветка схлопывается.

## Цель

Сохранить раскрытость ранее раскрытых узлов, находящихся за первой страницей уровня, при mutation reload, не превращая восстановление в безусловную полную загрузку всех страниц всех уровней.

## Главный принцип

НЕЛЬЗЯ решать задачу простым циклом:

```text
while (!LoadedAllChildren)
    LoadMoreChildrenAsync(parent)
```

для каждого раскрытого parent.

Это может превратить mutation reload небольшого дерева UI в чтение тысяч строк.

Нужно адресное или ограниченное восстановление только тех веток, которые действительно были раскрыты до reload.

## Сначала провести разведку

Перед изменением production-кода изучить:

- `Components/Tree/ClayTreeView.Mutations.cs`
- `Components/Tree/ClayTreeView.Loading.cs`
- `Components/Tree/ClayTreeView.Expansion.cs`
- `Components/Tree/ClayTreeView.State.cs` / актуальный state partial
- `Components/Tree/ClayTreeView.Filter.cs`
- `Components/Tree/DataSources/*`
- `Components/Tree/ClayTreeSqlBuilder.cs`
- `Components/Tree/ClayTreeData.cs`
- `Components/Tree/Models/ClayTreeNode.cs`
- механизмы `LoadAncestorChainAsync` / восстановления пути, если они существуют
- текущие tests paging/state/reload

Определить, можно ли переиспользовать существующий адресный механизм загрузки пути до конкретного `RawId`/Id.

Не писать вторую независимую систему ancestor lookup, если уже есть пригодная инфраструктура.

## Требуемое поведение

Если до mutation reload были раскрыты:

```text
Root
 └─ Child051
     └─ GrandChild
```

и `Child051` лежит за первой страницей, после reload:
- `Root` должен быть раскрыт;
- `Child051` должен быть найден/дозагружен адресно;
- `Child051` должен быть раскрыт;
- `GrandChild` должен быть восстановлен аналогично;
- нераскрытые siblings страниц 2..N не обязаны полностью материализовываться.

Если нужный узел удалён, перемещён или больше не является child старого parent, восстановление не должно падать.

## Архитектурное требование к snapshot

Текущий `HashSet<string> previouslyExpanded` хранит только Id и может быть недостаточен.

Разрешается заменить/дополнить snapshot минимальной структурой с устойчивыми значениями, например NodeId/RawId/ParentId/ancestor chain.

Но:
- не хранить старые `ClayTreeNode` как источник истины после reload;
- не переносить старые `Children`, `Parent`, L/R и другие node objects;
- snapshot должен содержать только стабильные значения, необходимые для повторного поиска.

## Предпочтительная стратегия

Если существующий `LoadAncestorChainAsync` или аналогичный метод способен адресно получить цепочку до node:
- переиспользовать его или выделить общий internal helper;
- не дублировать SQL;
- техническое восстановление expanded-state не должно менять persisted anchor/selection.

Если существующая paging-модель не позволяет безопасно вставить адресно найденный child в частично загруженный уровень, допускается bounded paging restore:
- догружать страницы только до страницы, содержащей нужный expanded child;
- остановиться сразу после нахождения последнего необходимого child для данного уровня.

## Paging semantics

После восстановления сохранить корректность:
- `LoadedAllChildren`
- `LastChildCursor`
- порядок `Children`
- отсутствие duplicates

Нельзя создать состояние, при котором последующий `LoadMoreChildrenAsync` пропускает или дублирует строки.

Если на одном parent сохранены несколько expanded children за первой страницей, не начинать загрузку с первой страницы заново для каждого child.

## Root и non-root

Исправление должно работать одинаково для:
- `ReloadLevelAsync(null)`;
- `ReloadLevelAsync(parent)`.

## Не менять

Не изменять:
- persistent `ClayTreeState` contract;
- пользовательскую семантику Expand/Collapse;
- DnD SQL;
- mutation SQL;
- `TableName`;
- filter semantics;
- public meaning `LevelPageSize`.

Не выполнять общий рефакторинг Tree.

## Тесты

Добавить regression tests минимум для:

1. Один expanded child на второй странице (`PageSize = 2`).
2. Несколько expanded children на разных страницах.
3. Удалённый child.
4. Перемещённый child.
5. Последующий пользовательский `LoadMore` не создаёт duplicates и продолжает с корректного cursor.
6. `LevelPageSize = 0` — без регрессий.
7. ParentKey — без лишней paging-логики.

Желательно fake `IClayTreeDataSource` со счётчиком запросов и контролируемыми страницами.

Тест должен доказывать отсутствие безусловной полной загрузки: если нужный child на странице 2 из 100, не должны загружаться все 100 страниц.

## Документация

Убрать/обновить известное ограничение CTFR2 в `Components/Tree/AGENTS.md`:

```text
Level paging: раскрытый потомок на странице 2+ не восстанавливается
```

Заменить описанием фактического алгоритма.

## Приёмка

В отчёте указать:

1. выбранную стратегию;
2. почему она не грузит весь уровень без необходимости;
3. как поддерживаются `LastChildCursor` и `LoadedAllChildren`;
4. как предотвращаются duplicates;
5. изменённые production-файлы;
6. добавленные tests;
7. результаты build/test;
8. root и non-root сценарии.

Не считать задачу выполненной только по тесту с `LevelPageSize=0`.
