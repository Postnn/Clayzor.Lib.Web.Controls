# CTFR2.2 — Ограничить paging restore и добавить behavioral regression tests

## Контекст

Репозиторий: `Postnn/Clayzor.Lib.Web.Controls`.

CTFR2.1 реализовал paging-aware восстановление раскрытых веток после mutation reload.

Текущая реализация:

- `CollectExpandedSnapshot` сохраняет `childId → parentId`;
- `RestoreExpandedAsync` сначала восстанавливает раскрытых детей из уже загруженной страницы;
- если нужный expanded child отсутствует, выполняется bounded paging через штатный `LoadMoreChildrenAsync(parent)`;
- страницы догружаются, пока все `missing` Id не найдены либо `parent.LoadedAllChildren == true`.

Это исправляет случай, когда раскрытый child находился на странице 2+.

Однако review CTFR2.1 обнаружил новый performance edge case.

## Проблема 1 — moved/deleted expanded child может вызвать полную загрузку уровня

Текущий алгоритм приблизительно такой:

```csharp
while (missing.Count > 0 && !parent.LoadedAllChildren)
{
    await LoadMoreChildrenAsync(parent);
    missing.RemoveWhere(...);
}
```

Если сохранённый expanded child после mutation:

- удалён;
- reparent-нут к другому parent;
- больше не существует в старом уровне,

то `missing` никогда не станет пустым.

В результате restore будет загружать страницы до:

```text
LoadedAllChildren == true
```

то есть потенциально материализует весь огромный уровень.

Пример:

```text
P1 expanded
 ├─ Child001
 ├─ ...
 ├─ Child50000
 └─ X expanded
```

До mutation `X` был загружен и раскрыт.

После:

```text
Reparent X: P1 → P2
```

snapshot содержит:

```text
X → P1
```

но после SQL `X` под `P1` больше нет.

Restore `P1` не должен читать десятки тысяч siblings, пытаясь найти уже перемещённый `X`.

## Цель

Сохранить преимущество CTFR2.1:

- expanded child на странице 2+ восстанавливается;

но одновременно гарантировать:

- restore никогда не читает больше данных, чем разумно требуется для восстановления UI-state, существовавшего до mutation;
- moved/deleted node не приводит к полной загрузке старого уровня.

## Предпочтительный инвариант

Mutation reload не должен материализовывать больше paging-данных уровня, чем было материализовано пользователем до mutation, кроме минимально необходимого адресного lookup, если существующая архитектура предоставляет такой безопасный механизм.

Особенно предпочтительный вариант:

помимо expanded mapping сохранить snapshot paging boundary каждого затронутого parent, например:

```text
ParentId
LastChildCursorBeforeReload
LoadedAllChildrenBeforeReload
ExpandedChildIds
```

После reload разрешается догружать страницы только до прежней фактически загруженной границы.

Если expanded child к этой границе не найден — считать, что после mutation он:

- удалён;
- перемещён;
- либо больше не принадлежит старому parent.

Не продолжать чтение до конца уровня.

Это направление не является обязательной конкретной реализацией. Если в существующем datasource есть более надёжный адресный lookup без нарушения paging metadata, можно использовать его.

## Сначала изучить текущий код

Обязательно просмотреть актуальные:

- `Components/Tree/ClayTreeView.Mutations.cs`
- `Components/Tree/ClayTreeView.Loading.cs`
- `Components/Tree/ClayTreeView.State.cs`
- `Components/Tree/ClayTreeView.Expansion.cs`
- `Components/Tree/DataSources/*`
- `Components/Tree/Models/ClayTreeNode.cs`
- paging tests
- CTFR2/CTFR2.1 tests

Не менять алгоритм вслепую.

## Требование к paging boundary

Если выбран snapshot прежней paging boundary, он должен быть устойчив к reload.

Нужно учитывать фактическую семантику cursor в текущем datasource.

Не предполагать автоматически, что cursor:
- является номером страницы;
- является индексом;
- можно сравнивать как integer.

Использовать фактический тип и контракт `LastChildCursor`.

Если безопасное сравнение cursor невозможно, можно сохранить число ранее материализованных children и ограничить восстановление этим количеством/числом страниц, если это соответствует текущему paging contract.

## Обязательные сценарии

### Сценарий A — expanded child был на странице 2

До reload:

```text
PageSize = 2

P
 ├─ A
 ├─ B
 ├─ C expanded
 │   └─ C1
 ├─ D
 ├─ ...
```

Пользователь ранее загрузил страницу с `C`.

После mutation `C` остаётся child `P`.

Ожидание:
- restore догружает страницу 2;
- находит `C`;
- раскрывает `C`;
- восстанавливает его ветку.

### Сценарий B — expanded child перемещён

До mutation:

```text
P1
 ├─ A
 ├─ B
 ├─ C expanded
```

После:

```text
C → P2
```

Ожидание:
- restore `P1` ищет `C` не дальше допустимой старой paging boundary;
- не загружает весь `P1`;
- не создаёт phantom `C`;
- не оставляет stale `C` в `_expanded`.

### Сценарий C — expanded child удалён

Поведение аналогично moved:
- bounded search;
- никаких исключений;
- никаких stale expanded Id;
- никакой полной загрузки уровня.

### Сценарий D — несколько expanded children

Если ранее под одним parent были раскрыты children на страницах 2 и 4:

```text
C expanded
G expanded
```

restore должен пройти paging один раз до необходимой прежней границы.

Нельзя:
- заново начинать paging для `C`;
- затем снова с начала для `G`.

### Сценарий E — пользователь загрузил много страниц

Если пользователь до mutation реально загрузил 10 страниц, допустимо восстановить до этой прежней границы.

Это не считается нежелательной полной загрузкой: данные уже были материализованы в UI до mutation.

Но если уровень имеет 1000 страниц, а пользователь видел только 10, restore не должен читать страницы 11..1000 только потому, что один snapshot Id больше не существует.

## Проблема 2 — root marker

CTFR2.1 использует:

```csharp
snapshot[root.Id] = "";
```

как специальный marker root.

Не использовать допустимое строковое значение доменного Id как служебный sentinel.

Исправить snapshot representation так, чтобы root выражался структурно.

Предпочтительные варианты:

```csharp
string? ParentId
```

где `null` означает root,

либо небольшой internal record:

```csharp
ExpandedNodeSnapshot(
    string NodeId,
    string? ParentId,
    ...
)
```

Не вводить magic string вроде:
- `""`
- `"ROOT"`
- `"__ROOT__"`

## Snapshot design

После CTFR2.2 snapshot должен содержать минимально необходимые immutable/value данные.

Не сохранять старые `ClayTreeNode` references для восстановления после reload.

Допустимо хранить:

- node Id;
- parent Id nullable;
- paging boundary parent-а;
- иные небольшие stable values, если они реально нужны алгоритму.

Не копировать:
- Children;
- Parent object;
- полный node graph;
- mutable UI state целиком.

## Критически важная часть — behavioral tests

Текущие CTFR2.1 tests проверяют в основном `CollectExpandedSnapshot`.

Этого недостаточно.

CTFR2.2 обязан добавить tests самого paging restore.

Если `RestoreExpandedAsync` сейчас private и тестировать его невозможно, разрешается:

1. выделить минимальный `internal` helper;
2. либо тестировать через `ReloadLevelAsync`/публичное поведение компонента;
3. использовать `InternalsVisibleTo`, если это уже принято в проекте.

Не делать production API public только ради tests.

## Fake datasource

Предпочтительно сделать deterministic fake datasource, который:

- имеет страницы фиксированного размера;
- считает количество load calls;
- фиксирует cursor каждого запроса;
- позволяет моделировать mutation между snapshot и reload;
- возвращает контролируемые children.

Tests должны проверять не только конечный `IsExpanded`, но и количество paging-запросов.

## Обязательные regression tests

### Test 1 — page 2 restored

`PageSize = 2`, expanded child находится на второй странице.

Проверить:
- child восстановлен;
- выполнено ровно необходимое число page loads;
- страницы после найденного child без необходимости не читаются.

### Test 2 — moved child does not scan full level

Сделать уровень, например, на 20+ страниц.

До mutation child был раскрыт в ранее загруженной области.

После mutation убрать его из старого parent.

Проверить:
- restore останавливается на прежней boundary;
- количество запросов существенно меньше полного числа страниц;
- `LoadedAllChildren` не становится true только из-за попытки найти moved child, если до mutation весь уровень не был загружен.

### Test 3 — deleted child does not scan full level

Аналогично Test 2.

### Test 4 — multiple expanded children share one paging pass

Два expanded child на разных ранее загруженных страницах одного parent.

Проверить отсутствие повторной загрузки одних и тех же страниц.

### Test 5 — subsequent user LoadMore remains correct

После restore вызвать обычный пользовательский `LoadMoreChildrenAsync`.

Проверить:
- cursor продолжает с правильной позиции;
- нет duplicate nodes;
- нет skipped nodes;
- порядок корректен.

### Test 6 — root reload

Проверить `ReloadLevelAsync(null)` с paging child page 2+.

### Test 7 — non-root reload

Проверить `ReloadLevelAsync(parent)` с paging child page 2+.

### Test 8 — paging disabled

`LevelPageSize = 0`.

Существующий CTFR2 behavior не должен измениться.

## Проверить `_expanded`

После moved/deleted scenario:

```text
_expanded
```

не должен содержать Id node, который не был фактически восстановлен.

Snapshot сам по себе не является основанием оставить Id в `_expanded`.

## Проверить HasChildren

Не менять `HasChildren` только ради технического поиска.

Если datasource после mutation показывает, что parent больше не имеет детей, restore должен спокойно завершиться.

## Не менять

Не изменять:

- persistent `ClayTreeState` contract;
- `LastExpandedId`;
- `SelectedIds`;
- DnD SQL;
- mutation SQL;
- `TableName`;
- `Schema`;
- filter semantics;
- публичную семантику `LevelPageSize`;
- пользовательские callbacks Expand/Collapse.

Техническое восстановление не должно вызывать пользовательский `OnNodeExpanded` и не должно сохранять persisted state для каждого восстановленного узла.

## Не делать

Запрещено:

- безусловно загружать уровень до `LoadedAllChildren`;
- делать отдельный SQL query на каждый missing Id, если уже можно использовать paging boundary;
- добавлять blanket `catch (Exception)`;
- скрывать реальные datasource errors;
- делать общий рефакторинг Tree;
- менять CTFR3 connectivity behavior.

## AGENTS.md

Обновить документацию CTFR2.1 так, чтобы она точно описывала новый предел восстановления.

Нужно явно зафиксировать:

- paging restore восстанавливает expanded nodes в пределах ранее материализованной области уровня;
- moved/deleted expanded nodes не вызывают полную загрузку уровня;
- root marker представлен структурно, без magic-string sentinel.

## Приёмка

В финальном отчёте обязательно указать:

1. какой snapshot теперь сохраняется;
2. как определяется максимальная допустимая paging boundary;
3. почему moved/deleted child не вызывает full scan;
4. как восстанавливается child page 2+;
5. как поддерживаются `LastChildCursor` и `LoadedAllChildren`;
6. почему последующий пользовательский `LoadMore` корректен;
7. как устранён `""` root sentinel;
8. список production changes;
9. список behavioral tests;
10. фактическое число paging calls в ключевых tests;
11. результаты build и полного test suite.

Не считать задачу выполненной, если добавлены только unit tests snapshot helper-а.
Обязательны tests реального paging restore behavior.
