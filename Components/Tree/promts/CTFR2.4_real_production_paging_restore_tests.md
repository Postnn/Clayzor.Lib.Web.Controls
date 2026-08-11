# CTFR2.4 — Только behavioral tests реального production paging restore

## ВАЖНО

Это ТОЛЬКО тестовая задача.

Production behavior CTFR2.3 уже принят.

Запрещено менять production-алгоритм `ClayTree` ради этой задачи, кроме минимального test seam, если без него невозможно вызвать реальный production path.

НЕЛЬЗЯ:
- переписывать `ReloadLevelAsync`;
- переписывать `RestoreExpandedAsync`;
- менять paging semantics;
- менять datasource;
- менять DnD;
- менять SQL;
- менять `ClayTreeState`;
- менять CTFR3 connectivity contract.

Цель CTFR2.4 — доказать тестами, что текущий production-код действительно работает.

---

# 1. Почему текущих tests недостаточно

Текущий `ClayTreePagingRestoreTests.DeepPageTwoRestore_LoadsMissingChild` вручную повторяет production-алгоритм внутри теста:

```csharp
var neededIds = ...
var missing = ...
var maxChildren = ...

while (...)
{
    var moreResult = await ds.LoadLevelAsync(...);
    ...
}
```

Такой test НЕ проверяет:

```csharp
ClayTreeView.ReloadLevelAsync(...)
ClayTreeView.RestoreExpandedAsync(...)
ClayTreeView.EnsureChildrenLoadedAsync(...)
ClayTreeView.LoadMoreChildrenAsync(...)
```

Он проверяет копию алгоритма, написанную в test assembly.

Если production `RestoreExpandedAsync` сломается, test может остаться зелёным.

CTFR2.4 должен устранить именно эту проблему.

---

# 2. Главный критерий

Behavioral test обязан проходить через реальный production call chain:

```text
ReloadLevelAsync
  -> LoadRootsAsync / EnsureChildrenLoadedAsync
  -> RestoreExpandedAsync
  -> LoadMoreChildrenAsync
  -> RestoreExpandedAsync recursively
```

Нельзя повторять эту логику вручную в тесте.

---

# 3. Как дать тестам доступ

Выбрать МИНИМАЛЬНЫЙ подход.

Предпочтительный порядок:

### Вариант A — internal test seam

Допустимо изменить access modifier:

```csharp
private Task ReloadLevelAsync(...)
```

на:

```csharp
internal Task ReloadLevelAsync(...)
```

ТОЛЬКО если проект уже использует `InternalsVisibleTo` для test assembly.

Аналогично можно сделать internal минимально необходимый helper.

Это НЕ считается изменением production semantics.

### Вариант B — internal wrapper только для tests

Например:

```csharp
internal Task TestReloadLevelAsync(ClayTreeNode? parent)
    => ReloadLevelAsync(parent);
```

Но не добавлять публичный API.

### Вариант C — reflection

Если менять access modifier нежелательно, тест может вызвать private method reflection-ом.

Reflection должна использоваться только как invocation mechanism.

Сам алгоритм нельзя копировать в test.

### Вариант D — component harness

Если существующая test infrastructure позволяет создать `ClayTreeView`, внедрить fake datasource и вызвать operation, которая естественно приводит к `ReloadLevelAsync`, это ещё лучше.

Выбрать наименее инвазивный вариант.

---

# 4. Fake datasource

Использовать `FakePagingDataSource` или улучшить существующий test fake.

Fake должен:

- реализовывать настоящий `IClayTreeDataSource`;
- поддерживать page size;
- поддерживать cursor;
- считать `LoadLevelAsync` calls по parent;
- позволять менять данные между pre-reload и reload;
- возвращать НОВЫЕ `ClayTreeNode` при reload, чтобы test действительно моделировал production lifecycle.

Не вызывать `LoadLevelAsync` вручную для имитации `RestoreExpandedAsync`.

После подготовки начального состояния все последующие paging calls должны происходить из production `ClayTreeView`.

---

# 5. Test harness должен использовать настоящий ClayTreeView

Нужно создать реальный instance `ClayTreeView` и установить необходимое внутреннее состояние.

Разрешается test-only setup через:
- subclass, если возможно;
- reflection;
- internal setters/helpers;
- bUnit component context, если проект его уже использует.

Не создавать отдельный fake `ClayTreeView`, который реализует собственный reload.

В test участвует production class:

```csharp
ClayTreeView
```

---

# 6. Test 1 — REAL root reload, deep page 2 restore

Обязательный test.

Исходное состояние:

```text
LevelPageSize = 2

Root = A expanded
 ├─ A1
 ├─ A2
 ├─ A3 expanded
 │   └─ X expanded
 └─ A4
```

До reload:
- у A реально materialized 4 children;
- `A3` находится на page 2;
- `A3.IsExpanded = true`;
- X загружен и раскрыт/находится в snapshot согласно текущей семантике.

Затем вызвать НАСТОЯЩИЙ:

```csharp
await ReloadLevelAsync(null);
```

через test seam.

После вызова найти НОВЫЕ node instances в текущем дереве.

Проверить:

```text
A.IsExpanded == true
A3 существует
A3.IsExpanded == true
```

и глубокое состояние X согласно ожидаемой CTFR2/CTFR2.3 семантике.

Проверить fake datasource call counts:

Для A должны реально произойти:
- page 1 load;
- page 2 load через production `LoadMoreChildrenAsync`.

Не делать эти два вызова вручную в test после старта reload.

---

# 7. Test 2 — REAL non-root reload, deep page 2 restore

Структура:

```text
P expanded
 └─ A expanded
     ├─ A1
     ├─ A2
     ├─ A3 expanded    <-- page 2
     └─ A4
```

Подготовить старое дерево так, чтобы A имел 4 materialized children.

Вызвать настоящий:

```csharp
await ReloadLevelAsync(P);
```

Проверить на НОВЫХ child instances:

```text
A restored
A.IsExpanded
A3 restored
A3.IsExpanded
```

Проверить реальные `LoadLevelAsync` calls для A.

---

# 8. Test 3 — REAL moved child, full scan запрещён

Структура datasource:

```text
PageSize = 2

A:
 page1 = A1,A2
 page2 = X,A4
 page3 = A5,A6
 ...
 page20
```

До mutation:
- пользователь загрузил только page1 + page2;
- X expanded;
- boundary A = 4.

Перед production reload изменить fake datasource:

```text
X больше НЕ child A
```

Например X reparented к B.

После этого вызвать настоящий reload уровня, который восстанавливает A.

Проверить:

- production пытается восстановить X;
- загружает максимум до старой boundary;
- НЕ читает page3..page20;
- `LoadCallCounts["A"]` соответствует bounded числу;
- X не находится в новом `A.Children`;
- X не остаётся в `_expanded`;
- `A.LoadedAllChildren` не должен становиться true только потому, что X исчез, если после boundary есть ещё данные.

Это ключевой regression test CTFR2.2.

---

# 9. Test 4 — REAL deleted child

Аналог Test 3.

До reload X существовал и был expanded в page2.

Перед reload удалить X из datasource.

Вызвать настоящий production reload.

Проверить:

- нет exception;
- нет full scan;
- нет phantom node;
- нет stale expanded Id;
- paging остановился на прежней materialized boundary.

---

# 10. Test 5 — REAL subsequent user LoadMore

После успешного production restore deep page2:

```text
A.Children содержит page1 + page2
```

Затем вызвать НАСТОЯЩИЙ production:

```csharp
LoadMoreChildrenAsync(A)
```

через test seam или реальное UI действие.

Datasource содержит page3.

Проверить:

- загружается именно page3;
- page1/page2 не повторяются;
- нет duplicate Id;
- нет skipped Id;
- порядок:
  `A1,A2,A3,A4,A5,A6`;
- `LastChildCursor` продолжает с правильной позиции;
- `LoadedAllChildren` соответствует datasource.

---

# 11. Test 6 — multiple expanded nodes, один paging pass

У A ранее materialized несколько страниц:

```text
page1: A1,A2
page2: A3(expanded),A4
page3: A5(expanded),A6
```

Оба `A3` и `A5` должны восстановиться.

После production reload проверить call count:

```text
A page1
A page2
A page3
```

Каждая страница загружена только один раз.

Не допускается:
- page1,page2 для A3;
- потом повторно page1,page2,page3 для A5.

---

# 12. Test 7 — paging disabled regression

`LevelPageSize = 0`.

Вызвать реальный root или non-root reload.

Проверить:
- deep expanded state сохраняется;
- никаких новых paging-specific ошибок;
- production path работает как до CTFR2.1.

---

# 13. Проверка новых instances

Tests должны доказать, что reload действительно пересоздал nodes.

Например сохранить:

```csharp
var oldA3 = ...
```

после reload:

```csharp
var newA3 = Find...
```

и проверить:

```csharp
Assert.NotSame(oldA3, newA3);
```

Иначе test может случайно проверять старое дерево, а не reload.

---

# 14. Проверка `_expanded`

После reload проверять не только `IsExpanded`.

Если test seam позволяет читать `_expanded`, проверить:

Для восстановленного узла:

```text
newA3.IsExpanded == true
_expanded содержит A3
```

Для moved/deleted:

```text
_expanded НЕ содержит X
```

Не добавлять public getter в production.

Использовать internal test access/reflection.

---

# 15. Проверка call counts

Fake datasource обязан позволить assert:

```text
LoadCallCounts[parentId]
```

Для ключевых tests в комментариях/названиях явно указать ожидаемые значения.

Пример:

```text
DeepPageTwo:
A = 2 loads during reload
  1 first page
  1 LoadMore
```

Для moved/deleted с прежней boundary = 4:

```text
A <= 2 loads during reload
```

и точно НЕ 20.

---

# 16. Не тестировать копию production logic

В test-файлах запрещены конструкции, повторяющие `RestoreExpandedAsync`, например:

```csharp
var missing = ...
while (missing.Count > 0 ...)
{
    await ds.LoadLevelAsync(...)
}
```

Если такой код остаётся из старого `DeepPageTwoRestore_LoadsMissingChild`:

- удалить этот test;
- либо переписать его на production-path.

Fake datasource может реализовать paging — это нормально.

Но orchestration restore должен выполняться только production `ClayTreeView`.

---

# 17. Production changes

Ожидается:

```text
0 semantic production changes
```

Допустимо только:

- `private -> internal` для testable method;
- internal test wrapper;
- `InternalsVisibleTo`, если его нет и он нужен.

Если агент считает, что production algorithm нужно менять — СТОП.

Не менять его в CTFR2.4.

В отчёте написать найденную проблему отдельно.

---

# 18. Не затрагивать CTFR3.1

Не добавлять в эту задачу tests DALC/connectivity.

CTFR3.1 — отдельная задача.

---

# 19. Имена tests

Использовать понятные имена, например:

```text
ReloadRoot_DeepExpandedChildOnPage2_RestoresViaProductionPaging
ReloadNonRoot_DeepExpandedChildOnPage2_RestoresViaProductionPaging
Reload_MovedExpandedChild_StopsAtPreviousBoundary
Reload_DeletedExpandedChild_StopsAtPreviousBoundary
Reload_AfterRestore_LoadMoreContinuesWithoutDuplicates
Reload_MultipleExpandedChildren_LoadsEachPageOnce
Reload_PagingDisabled_PreservesExpandedState
```

---

# 20. Финальная проверка агента

Перед коммитом:

### Проверка A

Есть ли test, который вызывает настоящий:

```text
ReloadLevelAsync(null)
```

?

Если НЕТ — задача не выполнена.

### Проверка B

Есть ли test, который вызывает настоящий:

```text
ReloadLevelAsync(non-null parent)
```

?

Если НЕТ — задача не выполнена.

### Проверка C

Есть ли test, где `LoadMoreChildrenAsync` вызывается production-кодом, а НЕ test-кодом?

Если НЕТ — задача не выполнена.

### Проверка D

Есть ли moved/deleted test с уровнем значительно длиннее старой boundary?

Если НЕТ — задача не выполнена.

### Проверка E

Проверяется ли subsequent production `LoadMoreChildrenAsync` после restore?

Если НЕТ — задача не выполнена.

---

# Приёмка

В финальном отчёте указать:

1. как tests вызывают настоящий `ReloadLevelAsync`;
2. какие test-only access changes сделаны;
3. подтверждение, что production algorithm не менялся;
4. root reload test;
5. non-root reload test;
6. deep page2 test;
7. moved child bounded test;
8. deleted child bounded test;
9. subsequent LoadMore test;
10. multiple-expanded one-pass test;
11. paging-disabled test;
12. exact datasource load-call counts;
13. подтверждение `Assert.NotSame` old/new nodes;
14. build result;
15. full test suite result.

CTFR2.4 нельзя считать выполненным, если test вручную воспроизводит цикл bounded paging вместо вызова production reload.
