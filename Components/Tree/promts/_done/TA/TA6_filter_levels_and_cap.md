# TA6 — Режим фильтра: неверные уровни узлов, рассинхрон `_expanded`, лимит показывает max+1

## Контекст

Файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.Filter.cs`. Три дефекта:

1. **`BuildTreeFromFlatNodes`: уровни считаются в произвольном порядке.** Цикл
   «Привязка детей к родителям» идёт по словарю `byParent`; выражение
   `child.Level = parentNode.Level + 1` использует `parentNode.Level`, который для глубоких цепочек
   ещё не проставлен (порядок словаря не гарантирован). В ParentKey-режиме (нет колонки уровня,
   все Level = 0 после маппинга) внук может получить Level = 1 → отступы дерева ломаются.
2. **`_expanded` не синхронизируется.** Узлы фильтр-набора приходят с `IsExpanded = true`
   (проставляет `ClaySqlTreeDataSource.LoadFilteredAsync`), но проекция `_expanded` не пополняется:
   `IClayTreeView.ExpandedIds` и последующие `CollapseNodeAsync` работают с рассинхронизированным
   набором.
3. **Лимит: показывается max+1 совпадений.** SQL берёт `TOP (@max + 1)` для детекта переполнения,
   но лишнее (max+1)-е совпадение не отбрасывается: `_filterMatchCount` считается по факту,
   `_filterCapped = _filterMatchCount > max`, и дерево честно рисует max+1 узлов, тогда как панель
   пишет «Отображаются только max». Попутно: XML-doc `ClayTreeOptions.MaxFilterRecords` обещает
   «0 — без лимита», а код молча заменяет 0 на 100.

Лимит чинится на стороне C# минимальным способом: если совпадений пришло больше max — выставить
`_filterCapped`, но НЕ строить дерево из лишнего совпадения. Лишнее совпадение — последнее в
порядке прихода (SQL сортирует Matches детерминированно). Удаляем его и всех «осиротевших»
предков, которые оказались в наборе только ради него.

## Шаги

### Шаг 1 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeView.Filter.cs`

**1.1. Пересчёт уровней BFS-обходом.** В конец `BuildTreeFromFlatNodes` (после цикла привязки
детей) добавить:

```csharp
// Уровни: цикл выше шёл в произвольном порядке словаря и мог посчитать Level
// до того, как проставлен Level родителя. Пересчитываем детерминированно от корней.
var queue = new Queue<ClayTreeNode>();
foreach (var root in _roots)
{
    root.Level = 0;
    queue.Enqueue(root);
}
while (queue.Count > 0)
{
    var current = queue.Dequeue();
    foreach (var child in current.Children)
    {
        child.Level = current.Level + 1;
        queue.Enqueue(child);
    }
}
```

Строку `child.Level = parentNode.Level + 1;` в цикле привязки при этом УДАЛИТЬ (уровень теперь
считает только BFS; двойного присваивания быть не должно).

**1.2. Синхронизация `_expanded`.** Там же, после BFS, добавить:

```csharp
// Проекция раскрытых: узлы фильтр-набора приходят с IsExpanded = true.
_expanded.Clear();
foreach (var node in _byId.Values)
{
    if (node.IsExpanded)
        _expanded.Add(node.Id);
}
```

**1.3. Отсечение лишнего совпадения.** В `ApplyFilterAsync`, заменить блок:

```csharp
_filterMatchCount = flatNodes.Count(n => n.IsMatch);
_filterCapped     = _filterMatchCount > max;

BuildTreeFromFlatNodes(flatNodes);
```

на:

```csharp
_filterMatchCount = flatNodes.Count(n => n.IsMatch);
_filterCapped     = _filterMatchCount > max;

if (_filterCapped)
{
    // SQL вернул max+1 совпадений для детекта переполнения — лишнее (последнее
    // по порядку прихода) в дерево не пускаем, вместе с предками-сиротами.
    flatNodes = TrimOverflowMatch(flatNodes);
    _filterMatchCount = max;
}

BuildTreeFromFlatNodes(flatNodes);
```

и добавить в конец файла приватный метод:

```csharp
/// <summary>
/// Убирает из плоского фильтр-набора последнее по порядку совпадение (переполнение TOP max+1)
/// и предков, которые были в наборе только ради него (нет других совпадений в поддереве).
/// </summary>
private static IReadOnlyList<ClayTreeNode> TrimOverflowMatch(IReadOnlyList<ClayTreeNode> flatNodes)
{
    // Последнее совпадение в порядке прихода
    ClayTreeNode? overflow = null;
    for (var i = flatNodes.Count - 1; i >= 0; i--)
    {
        if (flatNodes[i].IsMatch) { overflow = flatNodes[i]; break; }
    }
    if (overflow is null)
        return flatNodes;

    var byId = flatNodes.ToDictionary(n => n.Id);
    var removed = new HashSet<string> { overflow.Id };

    // Подъём по цепочке предков: удаляем предка, если у него нет других
    // совпадений среди оставшихся узлов поддерева.
    // Дешёвая проверка: предок остаётся, если среди flatNodes есть другой узел
    // (не удалённый), чей ParentId-путь проходит через него. Для простоты и
    // корректности считаем «детей» напрямую по ParentId.
    var childrenOf = new Dictionary<string, List<ClayTreeNode>>();
    foreach (var n in flatNodes)
    {
        var pk = DataSources.ClaySqlTreeDataSource.ToKey(n.ParentId);
        if (pk.Length == 0) continue;
        if (!childrenOf.TryGetValue(pk, out var list)) { list = []; childrenOf[pk] = list; }
        list.Add(n);
    }

    var currentKey = DataSources.ClaySqlTreeDataSource.ToKey(overflow.ParentId);
    while (currentKey.Length > 0 && byId.TryGetValue(currentKey, out var ancestor))
    {
        var hasLiveChild = childrenOf.TryGetValue(ancestor.Id, out var kids)
            && kids.Any(k => !removed.Contains(k.Id));
        if (ancestor.IsMatch || hasLiveChild)
            break; // предок нужен сам по себе или другим ветвям
        removed.Add(ancestor.Id);
        currentKey = DataSources.ClaySqlTreeDataSource.ToKey(ancestor.ParentId);
    }

    return flatNodes.Where(n => !removed.Contains(n.Id)).ToList();
}
```

Примечание для NestedSet: у узлов может не быть `ParentId` (колонка родителя опциональна) —
тогда `TrimOverflowMatch` удалит только само лишнее совпадение, предки останутся; их пометка
`HasMatchChildren` может остаться на один узел «щедрее» — допустимо, важна корректность счётчика
и текста панели.

### Шаг 2 — файл `Clayzor.Lib.Web.Controls/Components/Tree/ClayTreeOptions.cs`

Привести документацию `MaxFilterRecords` в соответствие с кодом. Заменить XML-doc на:

```csharp
/// <summary>
/// Максимум совпадений, показываемых при фильтре (предки не в счёт).
/// Значения ≤ 0 трактуются как 100 (лимит обязателен — режим фильтра строит набор целиком).
/// </summary>
public int MaxFilterRecords { get; set; } = 100;
```

## Критерии приёмки

- Юнит-тест `BuildTreeFromFlatNodes` (через рефлексию или выделить в internal-хелпер):
  плоский набор ParentKey из цепочки A → B → C, поданный в порядке [C, A, B],
  даёт Level: A=0, B=1, C=2.
- Тест `TrimOverflowMatch`: набор из 3 совпадений (max=2) — последний match и его
  единственный предок-сирота удалены; предок с двумя match-детьми остаётся.
- При переполнении на панели «Найдено более N…», в дереве ровно N пометок «(!)».
- `ExpandedIds` после применения фильтра равен множеству раскрытых узлов набора.
