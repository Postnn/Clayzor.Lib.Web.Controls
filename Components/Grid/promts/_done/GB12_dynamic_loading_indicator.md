> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md** и **GB3_export_progress_indicator.md** (индикатор долгих операций — общий с этим шагом класс `.clay-grid-busy`). Требует выполненных **GB8**, желательно **GB3** и **GB7**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB12 — в динамическом гриде нет индикации при группировке/сортировке/фильтрации

Прочитать перед началом: `Components/Grid/ClayGrid.razor` — блок заголовка (`_isExporting`),
корневой `<MudPaper … Style="position:relative" id="@Id">`, тег `<MudDataGrid … Loading="@Loading">`;
`Components/Grid/ClayGrid.razor.cs` — `[Parameter] public bool Loading`, `NotifyQueryChanged`;
`Components/Grid/ClayGrid.Dynamic.cs` — `LoadDynamicData` (это точка, где реально идёт запрос
в БД в динамическом режиме); `Components/Grid/ClayGridPageBase.cs` — как СТАТИЧЕСКАЯ страница
выставляет `Grid.Loading` вокруг `LoadData` (образец, к которому приводим динамику);
`GB3_export_progress_indicator.md` — `RunBusyAsync`, `_busyLabel`, `MudOverlay` + `.clay-grid-busy`.

## Дефект

`Loading` — `[Parameter]`. В статическом режиме его выставляет страница-хост:
`ClayGridPageBase` перед `LoadData` ставит `Grid.Loading = true`, после — `false`, и
`MudDataGrid Loading="@Loading"` рисует свой оверлей загрузки. В **динамическом** режиме
хоста-страницы нет: `Home.razor` — это просто `<ClayGrid Dynamic="true" … />`, `Loading`
никто не трогает, он всегда `false`. Поэтому группировка, сортировка, фильтрация,
пагинация — всё, что уходит в `LoadDynamicData` и делает SQL-запрос, — идёт без индикации.
На заметных объёмах грид просто «замирает».

Заказчик просит: пока такой индикатор не сделан отдельно, использовать **тот же режим ожидания,
что и в групповых операциях** — то есть общий оверлей `.clay-grid-busy`, введённый в GB3
(`RunBusyAsync`/`_busyLabel`/`MudOverlay`). Так индикация данных и экспорта выглядит одинаково.

Порядок с GB3: если GB3 уже выполнен — переиспользуем его инфраструктуру (ничего нового в CSS).
Если GB3 ещё не сделан — этот шаг всё равно вводит только `_isBusy`/`_busyLabel`/оверлей;
согласуй с GB3, чтобы не появилось двух разных индикаторов (см. «Не делай»).

## Изменить/создать

### 1. `ClayGrid.Dynamic.cs` — обернуть загрузку данных индикацией

`LoadDynamicData` — единственная точка динамического запроса. Обернуть её тело видимой
занятостью. Если GB3 выполнен — вызвать его `RunBusyAsync`; если нет — ввести локальный флаг
(GB3 потом сведёт к общему).

Вариант при выполненном GB3:

```csharp
private async Task LoadDynamicData(ClayDataQuery query)
{
    await RunBusyAsync("Загрузка данных…", async () =>
    {
        query.ExpandedGroups = _dynamicExpandedGroups;

        var dp = new DynamicParameters();
        dp.Add("search", $"%{query.SearchText}%");

        var searchWhere = query.BuildWhereClause(SearchColumns);
        var filterWhere = ClayCompositeSqlBuilder.Build(query.CompositeFilter, dp, _dynamicKnownColumns);
        var where       = ClayDataQuery.CombineWhere(searchWhere, filterWhere);

        if (query.GroupEnabled && query.GroupColumns.Count > 0)
            await LoadDynamicGroupedData(query, where, dp);
        else
            await LoadDynamicFlatData(query, where, dp);

        await SaveDynamicState();
    });
}
```

Подпись — «Загрузка данных…» (короткая, нейтральная; экспорт остаётся со своими подписями
из GB3). Тело метода не меняется, только оборачивается.

Вариант без GB3 (если он ещё не сделан) — ввести флаг здесь и оверлей в разметке (п. 2):

```csharp
private bool _isBusy;
private string? _busyLabel;

private async Task LoadDynamicData(ClayDataQuery query)
{
    _isBusy = true; _busyLabel = "Загрузка данных…";
    StateHasChanged();
    await Task.Yield();
    try { /* …существующее тело… */ }
    finally { _isBusy = false; _busyLabel = null; StateHasChanged(); }
}
```

При таком варианте в отчёте явно указать: GB3 обязан свести `_isExporting`, `_isBusy` и оба
`_busyLabel` к одному механизму, чтобы не осталось двух индикаторов.

### 2. `ClayGrid.razor` — оверлей (только если GB3 ещё не добавил его)

Если GB3 уже вставил `<MudOverlay Visible="@_isExporting" …>` с `.clay-grid-busy` — **ничего
не добавляй**, `RunBusyAsync` уже поднимает `_isExporting`. Если GB3 не сделан — добавить оверлей
внутрь корневого `<MudPaper … Style="position:relative">`, последним элементом:

```razor
<MudOverlay Visible="@_isBusy" Absolute="true" DarkBackground="true">
    <div class="clay-grid-busy">
        <MudProgressCircular Color="Color.Primary" Indeterminate="true" Size="Size.Large" />
        <span>@_busyLabel</span>
    </div>
</MudOverlay>
```

и класс `.clay-grid-busy` завести по образцу из GB3 (в `clay.css` после GB7, иначе в обе копии
`app.css`).

### 3. Не трогать `Loading` и `MudDataGrid`

Параметр `Loading` и `Loading="@Loading"` оставить как есть — на нём держится индикация
статического режима. В динамике его не задействуем: `RunBusyAsync`-оверлей заметнее и совпадает
с экспортом (требование заказчика). Дублировать индикацию (и `MudDataGrid.Loading`, и оверлей)
не нужно.

## Не делай

- Не выставляй `Loading = true` из динамического кода — это `[Parameter]`, менять параметр
  изнутри компонента нельзя (Blazor перезапишет при следующем рендере родителя, а тут родитель
  и есть хост). Индикация динамики — через оверлей.
- Не оборачивай в индикацию `LoadDynamicGroupChildIdsAsync` (догрузка ID для выбора) — она
  фоновая, оверлей на ней будет мигать при каждом рендере; заказчик просит индикацию
  пользовательских операций с данными, а не служебных догрузок.
- Не вводи второй визуальный стиль индикатора. Если GB3 сделан — используй его `RunBusyAsync`
  и `.clay-grid-busy`. Один индикатор на грид.
- Не оборачивай в оверлей debounce-поиск отдельно — он и так идёт через `NotifyQueryChanged` →
  `LoadDynamicData`, индикатор появится сам.
- Не трогай статический `ClayGridPageBase` и его работу с `Grid.Loading`.

## Проверка (ручная)

- `Kesco.App.Web.Inventory`, `?id=140`: перетащить колонку в трей группировки → на время
  запроса грид накрыт затемнением с индикатором «Загрузка данных…»; по завершении оверлей исчез;
- клик по заголовку → сортировка → тот же индикатор;
- открыть «Настраиваемый фильтр», применить условие → индикатор;
- листание страниц, смена размера страницы → индикатор;
- раскрытие/сворачивание группы → индикатор (операция идёт через `NotifyQueryChanged`);
- индикатор экспорта (GB3) и индикатор загрузки выглядят одинаково (один класс `.clay-grid-busy`);
- быстрый ввод в поиск → после паузы debounce индикатор появляется один раз, не мигает на
  каждый символ;
- MARS-исключения нет (GB8) при быстрых последовательных операциях;
- статический режим (`/medical-tests`): индикация загрузки как была (`MudDataGrid.Loading`),
  не задвоилась;
- тёмная тема → индикатор читаем;
- `dotnet build` + `dotnet test` — зелёные.
