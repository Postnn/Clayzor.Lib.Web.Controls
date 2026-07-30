# CTF5 — индикатор загрузки узла на месте шеврона (как в MudBlazor TreeView)

> Заплатка к `ClayTreeView`. Стиль исполнения — `/AGENTS.md`
> (**Think → Simplicity → Surgical → Goal-Driven**). Пользовательский текст — на русском.
> Git — только по указанию. Один заход.

## Симптом

При разворачивании узла индикатор загрузки — **только** глобальный оверлей `.clay-busy`
(весь экран затемняется). На уровне отдельного узла спиннера нет — нет обратной связи,
*какой именно* узел грузится. В MudBlazor TreeView индикатор встаёт **на место** шеврона
разворачиваемого узла — ширина позиции постоянна, текст не дёргается, понятно какой узел
в процессе загрузки.

## Корневая причина (по коду)

Свойство `ClayTreeNode.IsLoading` объявлено, но **нигде не устанавливается** — grep
по `IsLoading =` даёт 0 вхождений в проекте.

В `ClayTreeNodeView.razor` (строка 7–19) — **два** блока: шеврон ИЛИ распорка. Спиннера нет:

```razor
@if (Node.HasChildren)
{
    <MudIconButton Icon="@(Node.IsExpanded ? ExpandMore : ChevronRight)"
                   Size="Size.Small" OnClick="HandleToggle"
                   Class="clay-tree-node-toggle pa-0"
                   Style="width:22px;height:22px;min-width:22px" />
}
else
{
    <div class="clay-tree-node-spacer" style="width:22px;height:22px;min-width:22px"></div>
}
```

`EnsureChildrenLoadedAsync` в `ClayTreeView.Loading.cs` вызывает `RunBusyAsync`, которая
ставит глобальный `_isBusy = true` (оверлей), но флаг `node.IsLoading` не трогает.

## Решение

Задача из двух частей:

**A.** В `EnsureChildrenLoadedAsync` — выставлять `node.IsLoading = true/false`, чтобы
узел знал, что он грузится.

**B.** В `ClayTreeNodeView.razor` — в позиции 22×22 всегда **ровно один** элемент, по приоритету:
1. **грузится** (`IsLoading` и `ShowLoadingIndicator`) → неинтерактивный спиннер;
2. иначе **есть дети** (`HasChildren`) → шеврон-кнопка;
3. иначе → пустая распорка.

Спиннер во время загрузки — **не кнопка**: клик по нему не должен ничего запускать (узел уже
грузится). Поэтому это `MudProgressCircular` в контейнере той же ширины, а не иконка внутри
`MudIconButton`.

## Что сделать

### 1. `ClayTreeView.Loading.cs` — установка/снятие `IsLoading`

В методе `EnsureChildrenLoadedAsync` (строка 62–83) выставить флаг до загрузки и снять после:

```csharp
public async Task EnsureChildrenLoadedAsync(ClayTreeNode node)
{
    if (node.IsLoaded) return;
    if (!node.HasChildren) return;

    node.IsLoading = true;
    StateHasChanged();

    try
    {
        await RunBusyAsync("Загрузка…", async () =>
        {
            var result = await _dataSource.LoadLevelAsync(new ClayTreeLoadRequest(node));
            if (result.Error is not null)
            {
                _error = result.Error;
                await OnLoadError.InvokeAsync(result.Error);
            }
            else
            {
                node.Children.Clear();
                node.Children.AddRange(result.Nodes);
                IndexNodes(result.Nodes, node);
                node.IsLoaded = true;
            }
        });
    }
    finally
    {
        node.IsLoading = false;
        StateHasChanged();
    }
}
```

`try/finally` гарантирует снятие флага даже при ошибке загрузки.
Двойной `StateHasChanged` (здесь + в `RunBusyAsync`) допустим — Blazor схлопнет лишний
цикл рендеринга, если состояние не изменилось.

### 2. `ClayTreeNodeView.razor` — трёхветочный рендер

Заменить существующий блок `@if (Node.HasChildren) ... else ...` (строки 7–19) на:

```razor
@{
    var showSpinner = Node.IsLoading
        && Tree is ClayTreeView { Options.ShowLoadingIndicator: true };
}

@if (showSpinner)
{
    <div class="clay-tree-node-toggle-slot">
        <MudProgressCircular Color="Color.Primary" Indeterminate="true" Size="Size.Small" />
    </div>
}
else if (Node.HasChildren)
{
    <MudIconButton
        Icon="@(Node.IsExpanded ? Icons.Material.Filled.ExpandMore : Icons.Material.Filled.ChevronRight)"
        Size="Size.Small"
        OnClick="HandleToggle"
        Class="clay-tree-node-toggle pa-0"
        Style="width:22px;height:22px;min-width:22px" />
}
else
{
    <div class="clay-tree-node-spacer" style="width:22px;height:22px;min-width:22px"></div>
}
```

Ключевое: спиннер и распорка занимают ту же позицию 22×22, что и шеврон, и **исключают** друг
друга (`if/else if/else`) — второго элемента в строке нет.

### 3. Стиль слота — в `clay.css`, не inline

`MudProgressCircular` меньше 22×22 и по умолчанию прижмётся влево — в слоте его нужно
отцентровать, чтобы он визуально совпал с центром шеврона. Добавить в
`wwwroot/css/clay.css` рядом с прочими `clay-tree-*`:

```css
.clay-tree-node-toggle-slot {
    width: 22px;
    height: 22px;
    min-width: 22px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
}
```

Это **структурные** свойства (размеры, flex-центрирование) — StyleGuard их пропускает.
Цвет спиннера задаёт `Color.Primary` (палитра MudBlazor), своих hex-цветов не вводить.

Размеры 22×22 продублированы в трёх местах (шеврон inline, распорка inline, слот в CSS).
**Не рефакторить это в общий класс в рамках заплатки** — три числа в одном файле разметки
читаются нормально, а вынос в класс раздувает диф и трогает уже существующие inline-стили
шеврона/распорки, которые править не просили.

## Ловушки

- **Клик во время загрузки.** Спиннер — `div` + `MudProgressCircular`, не `MudIconButton`,
  поэтому кликов не принимает. Убедиться, что не осталось `OnClick`/`@onclick` на слоте.
- **`ShowLoadingIndicator: false`.** При выключенном индикаторе `showSpinner` всегда `false` —
  тогда во время загрузки в позиции остаётся обычный шеврон (кликабельный). Это приемлемо:
  повторный клик по грузящемуся узлу блокируется проверками `if (node.IsLoaded) return` и
  `if (!node.HasChildren) return` в начале `EnsureChildrenLoadedAsync`. Поведение при
  выключенном индикаторе не меняем.
- **Мелькание.** Загрузка уровня быстрая — спиннер может мелькнуть на доли секунды. Это
  нормально и совпадает с MudBlazor; искусственную задержку не добавлять.
- **`Tree is ClayTreeView { Options... }`** — сохранить ту же проверку паттерном (`Tree`
  объявлен как `IClayTreeView`, у него нет `Options`). Не заменять на прямое
  обращение `Tree.Options`.
- **Двойной `StateHasChanged`.** `RunBusyAsync` уже вызывает `StateHasChanged` при входе и
  выходе. В шаге 1 добавляется ещё пара вызовов вокруг неё. Это безопасно: Blazor
  детектит, что состояние не изменилось, и пропускает лишний рендер-цикл.

## Не делай

- Не трогай `ClayTreeView.Expansion.cs` — логика раскрытия/сворачивания корректна.
- Не трогай `clay-busy`/`RunBusyAsync` — это индикатор **первичной** загрузки всего дерева
  (`LoadRootsAsync`), к разворачиванию узла отношения не имеет и остаётся как есть.
- Не выноси 22×22 в общий класс, не переписывай шеврон/распорку сверх слияния со спиннером.
- Не добавляй анимаций/задержек, не меняй размер спиннера (`Size.Small`).
- Не вводи свои цвета — только палитра MudBlazor.

## Проверка

- `dotnet build Clayzor.sln` — зелёный, без новых warning'ов;
- `dotnet test` — зелёный (тесты не затрагиваются — это разметка и один флаг);
- ручной прогон `/tree-test`, оба режима, на узле с заметной задержкой загрузки (при
  необходимости временно замедлить `LoadLevelAsync` на стенде, потом вернуть):
  - клик по шеврону → на **месте** шеврона появляется спиннер, текст узла **не сдвигается**;
  - после загрузки спиннер сменяется шевроном `ExpandMore`, текст на том же месте;
  - лист без детей — распорка той же ширины, выравнивание с узлами-родителями сохранено;
  - повторный клик по грузящемуся узлу ничего не запускает (второго запроса в профайлере нет);
  - `ShowLoadingIndicator = false` → во время загрузки виден обычный шеврон, текст не прыгает;
  - тёмная тема — спиннер виден, цвет из палитры;
  - первичная загрузка дерева (`clay-busy`) выглядит как прежде — её не трогали.
  - глобальный оверлей `.clay-busy` по-прежнему показывается при загрузке уровня (не убирали).
