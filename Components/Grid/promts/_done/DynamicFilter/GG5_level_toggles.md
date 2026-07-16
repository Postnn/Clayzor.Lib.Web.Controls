> Часть плана «Группировка динамического грида». Перед началом прочитай **GG0_README_dynamic_grouping.md** и **_readme_grid_dynamic.md**. Требует выполненного **GG3**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GG5 — чипы лотка: «развернуть/свернуть все группы уровня»

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGridPageBase.Export.Selected.cs`, конец файла — **эталон**:
  `GetGroupKeysByDepth`, `CollectKeysByDepth`, `IClayGridDataLoader.IsLevelFullyExpanded`,
  `IClayGridDataLoader.ToggleLevelExpandedAsync`. Прочитай построчно.
- `Components/Grid/ClayGrid.razor` — лоток группировки, блок `@if (DataLoader is not null)`
  внутри чипа (`var depth = idx; var isExpanded = DataLoader.IsLevelFullyExpanded(depth);`).
- `Components/Grid/ClayGrid.Grouping.cs` — `ToggleLevel(int depth)`.
- `Components/Grid/ClayGrid.Dynamic.Grouping.cs` (GG2/GG3) — `_dynamicGroupRoots`,
  `_dynamicGroupKeysByDepth`, `_dynamicExpandedGroups`.
- `Components/Grid/ClayGroupingEngine.cs` — `GridGroupNode`, `GridGroupAgg.Depth`, `FullKey`.

## Задача

В лотке группировки у каждого чипа есть кнопка «развернуть/свернуть все группы этого уровня».
Она под условием:

```razor
@if (DataLoader is not null)
{
    var depth = idx;
    var isExpanded = DataLoader.IsLevelFullyExpanded(depth);
    <MudIconButton ... OnClick="async () => await ToggleLevel(depth)" ... />
}
```

и `ToggleLevel` тоже:

```csharp
private async Task ToggleLevel(int depth)
{
    if (DataLoader is not null)
    {
        await DataLoader.ToggleLevelExpandedAsync(depth);
        StateHasChanged();
    }
}
```

`IsLevelFullyExpanded` и `ToggleLevelExpandedAsync` объявлены в `IClayGridDataLoader`, то есть
доступны только через страницу. В динамическом режиме `DataLoader` — `null`, кнопки нет.

Нужны те же две операции внутри грида, поверх `_dynamicGroupRoots` и `_dynamicExpandedGroups`.

## Изменить/создать

**1.** `ClayGrid.Dynamic.Grouping.cs` — кеш ключей по глубине и две операции:

```csharp
    /// <summary>
    /// Словарь глубина → FullKey всех групп на этой глубине. Строится лениво по дереву
    /// последней загрузки; кеш сбрасывается в LoadDynamicGroupedData / LoadDynamicFlatData.
    /// </summary>
    private Dictionary<int, List<string>> GetDynamicGroupKeysByDepth()
    {
        if (_dynamicGroupKeysByDepth is not null) return _dynamicGroupKeysByDepth;
        _dynamicGroupKeysByDepth = new Dictionary<int, List<string>>();
        if (_dynamicGroupRoots is not null)
            CollectDynamicKeysByDepth(_dynamicGroupRoots, _dynamicGroupKeysByDepth);
        return _dynamicGroupKeysByDepth;
    }

    /// <summary>Рекурсивно собирает FullKey групп из дерева, раскладывая по глубине.</summary>
    private static void CollectDynamicKeysByDepth(
        List<GridGroupNode> nodes, Dictionary<int, List<string>> result)
    {
        foreach (var node in nodes)
        {
            var d = node.Aggregate.Depth;
            if (!result.ContainsKey(d)) result[d] = [];
            result[d].Add(node.Aggregate.FullKey);
            CollectDynamicKeysByDepth(node.Children, result);
        }
    }

    /// <summary>Развёрнуты ли ВСЕ группы на указанной глубине (динамический режим).</summary>
    private bool IsDynamicLevelFullyExpanded(int depth)
    {
        var map = GetDynamicGroupKeysByDepth();
        return map.TryGetValue(depth, out var keys) && keys.Count > 0
            && keys.All(k => _dynamicExpandedGroups.Contains(k));
    }

    /// <summary>
    /// Переключает ВСЕ группы на указанной глубине (динамический режим).
    /// Разворачивание каскадно раскрывает родительские уровни 0..depth-1, иначе
    /// раскрытые внутренние группы просто не будут видны под свёрнутым родителем.
    /// Сворачивание трогает только этот уровень.
    /// </summary>
    private async Task ToggleDynamicLevelExpanded(int depth)
    {
        var map = GetDynamicGroupKeysByDepth();
        if (!map.TryGetValue(depth, out var keys) || keys.Count == 0) return;

        bool allExpanded = keys.All(k => _dynamicExpandedGroups.Contains(k));

        if (allExpanded)
        {
            foreach (var k in keys) _dynamicExpandedGroups.Remove(k);
        }
        else
        {
            for (int d = 0; d <= depth; d++)
                if (map.TryGetValue(d, out var levelKeys))
                    foreach (var k in levelKeys) _dynamicExpandedGroups.Add(k);
        }

        _pageNumber = 1;
        await NotifyQueryChanged();
    }
```

Это построчная копия эталона из `ClayGridPageBase.Export.Selected.cs` с тремя заменами:
`_query.ExpandedGroups` → `_dynamicExpandedGroups`, `_groupTreeRoots` → `_dynamicGroupRoots`,
`LoadData()` → `NotifyQueryChanged()`. **Не «улучшай» логику по дороге** — поведение обоих
режимов должно совпадать.

`_pageNumber = 1` обязателен: после разворачивания уровня эффективных строк становится в разы
больше, и текущий номер страницы теряет смысл.

**2.** `ClayGrid.Grouping.cs`, `ToggleLevel` — диспетчер:

```csharp
    /// <summary>
    /// Переключает развёрнутость всех групп на заданной глубине.
    /// Динамический режим обрабатывает сам, статический — через DataLoader.
    /// </summary>
    private async Task ToggleLevel(int depth)
    {
        if (Dynamic)
        {
            await ToggleDynamicLevelExpanded(depth);
            StateHasChanged();
        }
        else if (DataLoader is not null)
        {
            await DataLoader.ToggleLevelExpandedAsync(depth);
            StateHasChanged();
        }
    }

    /// <summary>Развёрнуты ли все группы уровня — для иконки кнопки на чипе лотка.</summary>
    private bool IsLevelFullyExpanded(int depth)
        => Dynamic
            ? IsDynamicLevelFullyExpanded(depth)
            : DataLoader?.IsLevelFullyExpanded(depth) ?? false;
```

**3.** `ClayGrid.razor`, чип лотка — заменить условие и вызов:

```razor
@if (Dynamic || DataLoader is not null)
{
    var depth = idx;
    var isExpanded = IsLevelFullyExpanded(depth);
    <MudIconButton
        Icon="@(isExpanded ? Icons.Material.Filled.UnfoldLess : Icons.Material.Filled.UnfoldMore)"
        Size="Size.Small"
        OnClick="async () => await ToggleLevel(depth)"
        Class="pa-0 clay-grid-chip-btn--gold"
        title="@(isExpanded ? "Свернуть все группы" : "Развернуть все группы")" />
}
```

Разметка больше не зовёт `DataLoader` напрямую — только `IsLevelFullyExpanded`, которое само
разводит режимы. Иконку, класс, размер и тултип оставь ровно теми же.

## Не делай

Не добавляй `IsLevelFullyExpanded`/`ToggleLevelExpandedAsync` в `IClayGrid` — это внутренняя
кухня грида, наружу их никто не зовёт. Не трогай `IClayGridDataLoader` и его реализацию в
`ClayGridPageBase.Export.Selected.cs`. Не забудь, что `_dynamicGroupKeysByDepth` сбрасывается
в `null` в обеих ветках загрузки (GG2) — не кешируй его где-то ещё. Не включай группировку
(`Groupable` остаётся `false`) — это GG7.

## Проверка

**Ручная (временный хак из GG2/GG3: `_groupColumns.Add("КодТипа");` в конце `InitDynamicMode`).**

`?id=140&CLID=9`, размер страницы 10:

- в лотке группировки у чипа «Тип исследования» появилась кнопка со стрелками
  (`UnfoldMore` — все свёрнуты);
- клик → ВСЕ группы раскрылись, иконка сменилась на `UnfoldLess`, страница ушла на 1-ю,
  «Всего» = число групп + число всех записей;
- клик ещё раз → все группы свернулись, иконка вернулась;
- раскрыть одну группу вручную (шевроном) → иконка чипа осталась `UnfoldMore` (развёрнуты не все);
- раскрыть вручную ВСЕ группы → иконка чипа сама стала `UnfoldLess`;
- убрать колонку из лотка (крестик на чипе) → грид плоский, кнопки уровня нет, ошибок нет;
- вернуть колонку в лоток → дерево пересобралось, кнопка снова работает (кеш глубин сбросился).

Статический режим (`MedicalTests.razor`): кнопка на чипе работает как раньше — это главная
проверка шага, разметка чипа общая для обоих режимов.

Двухуровневую группировку на этом шаге проверить нельзя (перетаскивание второй колонки в лоток
недоступно, пока `Groupable = false`) — проверишь в GG7.
