> Часть плана «Группировка динамического грида». Перед началом прочитай **GG0_README_dynamic_grouping.md** и **_readme_grid_dynamic.md**. Требует выполненного **GG2**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GG3 — раскрытие групп: состояние и обработчик клика по шеврону

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGridPageBase.cs`, метод `ToggleGroup(GroupHeaderRow header)` —
  **эталон, включая автопереход страницы. Прочитай построчно, там неочевидная логика.**
- `Components/Grid/ClayDataQuery.cs` — `ExpandedGroups` (кто им владеет), `TotalCount`.
- `Components/Grid/ClayGrid.Grouping.cs` — `OnGroupToggle` (`EventCallback` [Parameter]),
  `_groupColumns`, `AddGroupColumn`, `RemoveGroupColumn`.
- `Components/Grid/ClayGrid.razor` — ВСЕ места с `<ClayGroupHeader Header="gh" OnToggle="OnGroupToggle" />`
  (их два: в сервисной колонке и в цикле по `_columnOrder`).
- `Components/Grid/ClayGroupHeader.razor` — что именно вызывается по клику.
- `Components/Grid/ClayGrid.razor.cs` — `NotifyQueryChanged` (как собирается `ClayDataQuery`),
  `_pageNumber`, `_totalPages`.
- `Kesco.App.Web.Inventory/Components/Pages/Home.razor` — убедись, что `OnGroupToggle` там
  не подписан.

## Задача

Кто хранит «какие группы раскрыты»:

- **статический режим**: `ClayGridPageBase._query.ExpandedGroups`. Страница подписывает
  `OnGroupToggle="ToggleGroup"` на теге `<ClayGrid>`, грид дёргает `EventCallback`, страница
  меняет своё состояние и перезагружает данные;
- **динамический режим**: страницы нет. `Home.razor` — это `<ClayGrid Dynamic="true" />` и
  больше ничего, `OnGroupToggle` не подписан. Клик по шеврону уходит в пустой `EventCallback`
  и не делает НИЧЕГО.

Плюс `NotifyQueryChanged` собирает `ClayDataQuery` без `ExpandedGroups` — в статике их
подставляет страница в своё `_query`, а `GG2` читает `query.ExpandedGroups` и всегда получает
пустой набор.

Значит, в динамическом режиме владельцем состояния становится сам грид.

## Изменить/создать

**1.** `ClayGrid.Dynamic.Grouping.cs` — поле состояния и обработчик:

```csharp
    /// <summary>
    /// Раскрытые группы (полные ключи через \u001F) в динамическом режиме.
    /// В статическом режиме тем же владеет ClayGridPageBase._query.ExpandedGroups.
    /// </summary>
    private readonly HashSet<string> _dynamicExpandedGroups = [];

    /// <summary>
    /// Раскрывает/сворачивает группу в динамическом режиме.
    /// Копия логики ClayGridPageBase.ToggleGroup, включая автопереход страницы.
    /// </summary>
    private async Task ToggleDynamicGroup(GroupHeaderRow header)
    {
        var wasExpanded = _dynamicExpandedGroups.Contains(header.FullKey);
        if (wasExpanded)
            _dynamicExpandedGroups.Remove(header.FullKey);
        else
            _dynamicExpandedGroups.Add(header.FullKey);

        await NotifyQueryChanged();

        if (!wasExpanded)
        {
            // Раскрыли последнюю группу на странице: её детали физически не влезли —
            // сразу уходим на следующую страницу, иначе клик выглядит как «не сработал».
            var expandedHeader = (Items ?? []).OfType<GroupHeaderRow>()
                .FirstOrDefault(h => h.FullKey == header.FullKey);
            if (expandedHeader is not null)
            {
                var rows      = (Items ?? []).ToList();
                var headerIdx = rows.IndexOf((TEntity)(object)expandedHeader);
                if (headerIdx >= 0 && headerIdx == rows.Count - 1 && header.ItemCount > 0)
                {
                    _pageNumber++;
                    await NotifyQueryChanged();
                }
            }
        }
        else if (TotalCount > 0 && _pageNumber > _totalPages)
        {
            // Свернули группу: эффективных строк стало меньше, текущей страницы может уже не быть.
            _pageNumber = _totalPages;
            await NotifyQueryChanged();
        }

        await InvokeAsync(StateHasChanged);
    }
```

Про автопереход: это не украшение. Раскрытая группа занимает 1 строку заголовка + N строк
деталей. Если заголовок оказался последней строкой страницы, детали уехали на следующую и
пользователь видит «щёлкнул — ничего не изменилось». Статический режим решает это переходом
вперёд, динамический обязан вести себя так же. **Не выкидывай этот блок как «лишний».**

Отличия от эталона `ClayGridPageBase.ToggleGroup`, которые нужно понимать:

- вместо `LoadData()` — `NotifyQueryChanged()` (это единственный вход в загрузку у грида);
- вместо `_rows` — `Items`, вместо `_query.PageNumber` — `_pageNumber`,
  вместо `_query.TotalCount` — `TotalCount`;
- `maxPage` считать не надо: у грида уже есть `_totalPages` (`ClayGrid.Paging.cs`) с той же
  формулой.

**2.** `ClayGrid.Dynamic.Grouping.cs` — прокинуть состояние в запрос. `NotifyQueryChanged`
общий для обоих режимов и `ExpandedGroups` не заполняет, поэтому подставляй их в начале
динамической загрузки. В `ClayGrid.Dynamic.cs`, `LoadDynamicData`, ПЕРВОЙ строкой:

```csharp
private async Task LoadDynamicData(ClayDataQuery query)
{
    // NotifyQueryChanged собирает query без ExpandedGroups (в статике их владелец — страница).
    query.ExpandedGroups = _dynamicExpandedGroups;

    var dp = new DynamicParameters();
    /* … дальше как после GG2 … */
}
```

Присваивается ссылка, не копия — так `WalkTree` и `ComputeEffectiveRows` видят актуальный набор.

**3.** `ClayGrid.Grouping.cs` — единый диспетчер клика (тот же приём, что `HandleRowEditClick`
в `ClayGrid.Dynamic.cs`):

```csharp
    /// <summary>
    /// Единый обработчик клика по шеврону заголовка группы.
    /// Динамический режим обрабатывает сам; статический — отдаёт странице через OnGroupToggle.
    /// </summary>
    private async Task HandleGroupToggle(GroupHeaderRow header)
    {
        if (Dynamic)
            await ToggleDynamicGroup(header);
        else
            await OnGroupToggle.InvokeAsync(header);
    }
```

**4.** `ClayGrid.razor` — в ОБОИХ местах заменить `OnToggle="OnGroupToggle"` на
`OnToggle="HandleGroupToggle"`. Их два: в `CellTemplate` сервисной колонки
(`@if (GroupRowHostKey == "__edit__")`) и в `CellTemplate` цикла по `_columnOrder`
(`@if (IsGroupRowHost(sqlName))`). **Найди оба, пропустишь один — половина гридов не будет
раскрываться.**

**5.** `[Parameter] public EventCallback<GroupHeaderRow> OnGroupToggle` оставить как есть —
это публичный контракт статического режима.

## Не делай

Не сохраняй `_dynamicExpandedGroups` в `vwНастройки`. Раскрытые группы — это состояние сессии,
а не настройка; в списке параметров G7 (`cols`/`flt`/`grp`/`srt`/`pgs`) его нет, и в статическом
режиме оно тоже не переживает F5. Не трогай `SaveDynamicState`/`RestoreDynamicState`.
Не включай группировку (`Groupable` остаётся `false`) — это GG7. Не трогай `GroupRowHostKey` —
это GG4. Не меняй `ClayGridPageBase.ToggleGroup`.

## Проверка

**Ручная (тот же временный хак, что в GG2 — вернуть его на время проверки):** в `InitDynamicMode`
перед `await NotifyQueryChanged();` временно добавить `_groupColumns.Add("КодТипа");`.

`?id=140&CLID=9`, размер страницы поставить 5, чтобы страницы были короткие:

- клик по шеврону группы → группа раскрылась, под заголовком появились строки детализации,
  в профайлере — детальный запрос с `ROW_NUMBER()` и `КодТипа = @dk0`;
- «Всего: N записей» выросло на `ItemCount` раскрытой группы;
- клик ещё раз → группа свернулась, строки исчезли, «Всего» вернулось;
- раскрыть группу, которая является ПОСЛЕДНЕЙ строкой на странице → грид сам ушёл на
  следующую страницу и показал её детали (автопереход);
- раскрыть несколько групп, уйти на последнюю страницу, свернуть все → номер страницы
  подтянулся к новому `_totalPages`, пустой страницы нет;
- раскрыть группу, перейти на страницу 2 и обратно → группа осталась раскрытой;
- раскрыть группу, нажать F5 → все группы свёрнуты (состояние сессии не сохраняется — так
  и задумано);
- детали внутри группы отсортированы по `DefaultOrder`, а не по колонке группировки
  (`BuildDetailOrder`);
- **убрать временную строку**, пересобрать, `?id=140` → грид плоский, клик по шеврону
  недостижим (заголовков групп нет).

Статический режим (`MedicalTests.razor`): группировка, раскрытие, автопереход страницы —
всё как раньше. Это главная проверка шага: `HandleGroupToggle` не должен сломать
`OnGroupToggle="ToggleGroup"`.
