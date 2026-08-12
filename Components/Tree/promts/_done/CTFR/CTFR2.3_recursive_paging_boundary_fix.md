# CTFR2.3 — Рекурсивно сохранять paging boundary для КАЖДОГО раскрытого parent

## ВАЖНО

Это узкий corrective task поверх CTFR2.2.

НЕ переписывай весь алгоритм.
НЕ придумывай новую архитектуру.
НЕ трогай DALC, DnD SQL, mutation SQL, persistent state или фильтрацию.

Нужно исправить ОДИН конкретный дефект CTFR2.2 и добавить tests, которые реально доказывают исправление.

---

# 1. Что сейчас сломано

В CTFR2.2 появились две структуры:

```csharp
Dictionary<string, string?> previouslyExpanded;
Dictionary<string, int> pagingBoundary;
```

`previouslyExpanded` собирается рекурсивно через:

```csharp
CollectExpandedSnapshot(...)
```

и поэтому знает глубокие связи:

```text
A  -> Root
A3 -> A
X  -> A3
```

НО `pagingBoundary` сейчас заполняется НЕ рекурсивно.

Для non-root reload:

```csharp
pagingBoundary[parent.Id] = parent.Children.Count;
```

Для root reload:

```csharp
foreach (var root in _roots)
    pagingBoundary[root.Id] = root.Children.Count;
```

То есть boundary есть только для:
- непосредственно reload-нутого parent;
- либо root nodes.

Для глубоких раскрытых родителей boundary НЕТ.

---

# 2. Конкретный воспроизводимый дефект

Пусть:

```text
LevelPageSize = 2

Root expanded
 └─ A expanded
     ├─ A1
     ├─ A2
     ├─ A3 expanded       <-- был загружен со страницы 2
     │   └─ X expanded
     └─ A4
```

До reload у `A` реально загружено 4 child:

```text
A.Children.Count == 4
```

Snapshot раскрытости содержит:

```text
A  -> Root
A3 -> A
X  -> A3
```

Но текущий `pagingBoundary` содержит только примерно:

```text
Root -> 1
```

или boundary внешнего reload-parent.

Записи:

```text
A -> 4
```

НЕТ.

После reload:

1. `A` восстанавливается;
2. `EnsureChildrenLoadedAsync(A)` грузит первую страницу:
   `A1`, `A2`;
3. вызывается:

```csharp
RestoreExpandedAsync(A, snapshot, pagingBoundary);
```

4. код делает:

```csharp
var maxChildren = pagingBoundary.GetValueOrDefault(A.Id, 0);
```

5. получает:

```text
maxChildren = 0
```

6. условие:

```csharp
parent.Children.Count < maxChildren
```

становится:

```text
2 < 0
```

то есть `false`.

Страница 2 НЕ загружается.

`A3` НЕ восстанавливается.

Следовательно CTFR2.2 сейчас работает только для paging первого уровня восстановления и не работает рекурсивно на глубине.

---

# 3. Что требуется сделать

Paging boundary должна сохраняться РЕКУРСИВНО для каждого parent, чьи раскрытые descendants могут потребовать paging restore.

Главный инвариант:

> Если snapshot содержит раскрытого child с `ParentId = P`, то snapshot paging state должен содержать прежнюю paging boundary для `P`.

Иными словами:

```text
expanded child C -> parent P
```

ОБЯЗАТЕЛЬНО означает наличие:

```text
pagingBoundary[P]
```

если P существовал в старом загруженном дереве.

---

# 4. Исправить сбор snapshot

Сейчас helper примерно такой:

```csharp
CollectExpandedSnapshot(
    ClayTreeNode parentNode,
    Dictionary<string, string?> snapshot)
```

Он должен собирать НЕ ТОЛЬКО expanded mapping, но и boundary каждого рекурсивно посещённого раскрытого parent.

Допустимый простой вариант:

```csharp
internal static void CollectExpandedSnapshot(
    ClayTreeNode parentNode,
    Dictionary<string, string?> expanded,
    Dictionary<string, int> pagingBoundary)
{
    pagingBoundary[parentNode.Id] = parentNode.Children.Count;

    foreach (var child in parentNode.Children)
    {
        if (!child.IsExpanded)
            continue;

        expanded[child.Id] = parentNode.Id;

        CollectExpandedSnapshot(
            child,
            expanded,
            pagingBoundary);
    }
}
```

Это ПРИМЕР ожидаемой логики, а не требование скопировать код буквально.

Но результат должен быть именно таким.

Для дерева:

```text
Root expanded
 └─ A expanded
     └─ A3 expanded
```

должно получиться минимум:

```text
previouslyExpanded:
    Root -> null
    A    -> Root
    A3   -> A

pagingBoundary:
    Root -> Root.Children.Count
    A    -> A.Children.Count
    A3   -> A3.Children.Count
```

Если `A3` раскрыт, его boundary тоже нужна, потому что при восстановлении его children может понадобиться paging.

---

# 5. Root reload

Для:

```csharp
ReloadLevelAsync(null)
```

НЕ ограничивай сбор boundary только этим:

```csharp
foreach (var root in _roots)
    pagingBoundary[root.Id] = root.Children.Count;
```

Нужно:

1. сохранить root marker:

```text
root.Id -> null
```

2. сохранить boundary root;
3. РЕКУРСИВНО собрать:
   - expanded descendants;
   - boundary каждого раскрытого descendant-parent.

Например:

```text
Root
 └─ A
     └─ A3
```

boundary должны быть сохранены для:

```text
Root
A
A3
```

если эти nodes раскрыты и их children были материализованы.

---

# 6. Non-root reload

Для:

```csharp
ReloadLevelAsync(parent)
```

также требуется рекурсивный snapshot.

Если:

```text
parent = P

P
 └─ A expanded
     ├─ A1
     ├─ A2
     └─ A3 expanded   <-- page 2
```

до очистки `P.Children` нужно сохранить:

```text
pagingBoundary[P]
pagingBoundary[A]
pagingBoundary[A3]
```

по тем же правилам.

Не должно быть отдельной урезанной логики для non-root.

---

# 7. НЕ сохранять boundary только для child

Не перепутай направление.

Если:

```text
A3 -> A
```

то paging boundary нужна для:

```text
A
```

потому что именно `A.Children` нужно догружать, чтобы найти `A3`.

Не поможет хранение только:

```text
pagingBoundary[A3]
```

Оно понадобится уже на следующем рекурсивном уровне — для поиска children самого `A3`.

---

# 8. CTFR2.2 full-scan protection сохранить

НЕ удалять ограничение:

```csharp
parent.Children.Count < maxChildren
```

или эквивалентную bounded логику.

Цель CTFR2.3 — НЕ вернуться к CTFR2.1, где moved/deleted child мог вызвать чтение уровня до `LoadedAllChildren`.

После исправления одновременно должны выполняться ДВА свойства:

### Свойство A

Expanded child на глубокой странице восстанавливается:

```text
Root
 └─ A
     └─ page 2: A3 expanded
```

### Свойство B

Если `A3` после mutation удалён/перемещён, restore читает только до прежней boundary `A`, а НЕ до конца всего уровня.

---

# 9. Root sentinel

Сохранить исправление CTFR2.2:

```csharp
string? ParentId
```

и:

```text
null = root
```

НЕ возвращать:
- `""`;
- `"ROOT"`;
- `"__ROOT__"`;
- любой другой magic string.

---

# 10. Behavioral tests ОБЯЗАТЕЛЬНЫ

Это критическая часть задачи.

Предыдущий дефект прошёл именно потому, что tests проверяли snapshot, но не глубокий paging restore.

Нельзя закрывать CTFR2.3 только tests helper-а.

Нужны tests реального восстановления.

---

# 11. Test 1 — глубокий page 2 restore

Минимальная структура:

```text
LevelPageSize = 2

Root expanded
 └─ A expanded
     ├─ A1
     ├─ A2
     ├─ A3 expanded
     │   └─ X expanded
     └─ A4
```

До mutation должны быть реально материализованы:

```text
A1
A2
A3
A4
```

После root reload первая загрузка `A` возвращает только:

```text
A1
A2
```

Restore обязан выполнить `LoadMoreChildrenAsync(A)` и получить:

```text
A3
A4
```

После восстановления проверить:

```text
Root.IsExpanded == true
A.IsExpanded == true
A3.IsExpanded == true
X.IsExpanded == true   // если X должен быть раскрыт по snapshot
```

И:

```text
_expanded
```

содержит реально восстановленные Id.

---

# 12. Test 2 — проверить snapshot boundary глубины

Отдельный unit test helper-а допустим ДОПОЛНИТЕЛЬНО.

Для:

```text
Root
 └─ A expanded
     └─ A3 expanded
```

проверить:

```text
pagingBoundary.ContainsKey(Root.Id)
pagingBoundary.ContainsKey(A.Id)
pagingBoundary.ContainsKey(A3.Id)
```

И значения равны фактическому количеству материализованных children ДО reload.

Этот test НЕ заменяет Test 1.

---

# 13. Test 3 — non-root deep paging

Структура:

```text
P
 └─ A expanded
     ├─ A1
     ├─ A2
     ├─ A3 expanded   <-- page 2
```

Вызвать реальный:

```text
ReloadLevelAsync(P)
```

После reload `A3` должен остаться раскрытым.

Проверить, что boundary `A` была использована.

---

# 14. Test 4 — moved child на глубине НЕ вызывает full scan

Сделать:

```text
LevelPageSize = 2

Root
 └─ A expanded
     ├─ page 1
     ├─ page 2: X expanded
     ├─ page 3
     ├─ ...
     └─ page 20
```

До mutation пользователь реально загрузил ТОЛЬКО первые 2 страницы.

Значит:

```text
pagingBoundary[A] == 4
```

После mutation `X` перемещён из `A`.

После reload:
- первая страница загружена;
- разрешено догрузить максимум до прежней materialized boundary;
- страницы 3..20 НЕ должны читаться;
- `X` не должен оставаться в `_expanded`;
- никаких phantom nodes.

Fake datasource должен считать page-load calls.

Проверить конкретное ограниченное количество запросов.

---

# 15. Test 5 — deleted child на глубине

То же самое, но `X` удалён.

Restore:
- не падает;
- не сканирует весь уровень;
- не оставляет stale expanded Id.

---

# 16. Test 6 — subsequent LoadMore

После успешного deep restore:

```text
A.Children = page1 + page2
```

вызвать обычный пользовательский:

```csharp
LoadMoreChildrenAsync(A)
```

Проверить:

- загружается следующая страница;
- `LastChildCursor` корректен;
- нет duplicates;
- нет skipped children;
- порядок children корректен.

---

# 17. Не подменять behavioral tests reflection-тестом алгоритма

Можно использовать:
- fake datasource;
- test subclass;
- internal helper;
- `InternalsVisibleTo`;
- reflection для вызова private method, если в проекте это уже принято.

Но test должен реально пройти через:
- reload;
- первую страницу;
- `LoadMoreChildrenAsync`;
- recursive restore.

Простой вызов:

```csharp
CollectExpandedSnapshot(...)
```

не является достаточным доказательством.

---

# 18. Внимательно с boundary = Children.Count

Если остаётся:

```csharp
pagingBoundary[parent.Id] = parent.Children.Count;
```

проверь сценарий:

```text
до reload: 3 children materialized
PageSize = 2
```

После первой страницы:

```text
Children.Count = 2
maxChildren = 3
```

один `LoadMore` может добавить ещё 2 и получить:

```text
Children.Count = 4
```

Это ДОПУСТИМО, если paging API загружает страницу атомарно.

Не пытайся обрезать загруженную страницу вручную.

Главный инвариант:
- не начинать загрузку СЛЕДУЮЩЕЙ страницы после достижения/пересечения старой boundary.

---

# 19. Не менять production semantics вне этой задачи

НЕ менять:

- `ClayTreeState`;
- `LastExpandedId`;
- `SelectedIds`;
- mutation error handling;
- CTFR3 connectivity contract;
- `SaveStateAsync`;
- DnD behavior;
- SQL;
- `ClayTreeOptions`;
- `LevelPageSize`;
- datasource public interfaces без крайней необходимости.

---

# 20. AGENTS.md

Обновить только если описание CTFR2.2 стало неточным.

Документация должна явно говорить:

> Paging boundary сохраняется рекурсивно для каждого раскрытого parent, поэтому глубокие expanded nodes на страницах 2+ восстанавливаются, а moved/deleted nodes не вызывают чтение дальше ранее материализованной области соответствующего уровня.

---

# 21. Финальная самопроверка агента

Перед коммитом ответь себе на эти вопросы:

### Вопрос 1

Для:

```text
Root -> A -> A3
```

есть ли после snapshot:

```text
pagingBoundary[A]
```

?

Если НЕТ — задача НЕ выполнена.

### Вопрос 2

Если `A3` лежит на page 2, вызовет ли:

```csharp
RestoreExpandedAsync(A, ...)
```

хотя бы один:

```csharp
LoadMoreChildrenAsync(A)
```

?

Если НЕТ — задача НЕ выполнена.

### Вопрос 3

Если `A3` удалён, остановится ли restore после прежней boundary `A`, а не после конца уровня?

Если НЕТ — задача НЕ выполнена.

### Вопрос 4

Есть ли behavioral test глубины минимум:

```text
Root -> A -> page2 A3
```

?

Если НЕТ — задача НЕ выполнена.

---

# Приёмка

В финальном отчёте обязательно указать:

1. какие production-файлы изменены;
2. как теперь рекурсивно собирается `pagingBoundary`;
3. пример snapshot для `Root -> A -> A3`;
4. почему `pagingBoundary[A]` гарантированно существует;
5. как deep page-2 node восстанавливается;
6. почему moved/deleted deep node не вызывает full scan;
7. количество page-load calls в behavioral tests;
8. root reload test;
9. non-root reload test;
10. subsequent `LoadMore` test;
11. результаты build;
12. результаты полного test suite.

Не писать в отчёте «bounded paging исправлен», если нет behavioral test, реально вызывающего paging на глубине > 1.
