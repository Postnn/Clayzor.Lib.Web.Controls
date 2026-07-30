> Часть серии **CTP**. Прочитать `CTP0_README_level_paging.md` и результат **CTP1**.
> Делать ТОЛЬКО этот шаг.

# CTP2 — UI догрузки уровня: кнопка (Button) и автоподгрузка (Scroll)

Загрузка порций работает (CTP1). Здесь — триггер в интерфейсе: в конце пагинируемого уровня
либо кнопка «Загрузить ещё N» (`Button`), либо невидимый сентинел, срабатывающий при доскролле
(`Scroll`, через `IntersectionObserver`). SQL и загрузка — общие из CTP1, разветвление только
в разметке.

## Прочитать

- результат **CTP1** — `LoadMoreChildrenAsync`, `LoadedAllChildren`, `LastChildCursor`,
  `LevelPagingMode`, `LevelPageSize`;
- `Components/Tree/ClayTreeNodeView.razor` — где рендерятся дети узла (`@foreach ... Children`);
- существующий JS-интероп библиотеки (у грида — прокрутка/drag&drop): найти `.js`-файл, как он
  подключается (`wwwroot`, `IJSRuntime`, `import`/глобальный объект), как оформлены
  `DotNetObjectReference` и `[JSInvokable]`. **Повторить стиль**, не заводить свой подход;
- `Components/Tree/ClayTreeView.razor.cs` — жизненный цикл, `IDisposable` (если есть).

## Где рисуется догрузка

В `ClayTreeNodeView.razor`, **после** `@foreach` по `Node.Children`, внутри блока раскрытого
узла — «хвост уровня». Показывается, только если по уровню есть ещё данные:

```razor
@if (Node.IsExpanded && Node.Children.Count > 0)
{
    @foreach (var child in Node.Children) { <ClayTreeNodeView ... /> }

    @if (ShowLevelPagingTail)
    {
        @* Button или Scroll — см. ниже *@
    }
}
```

`ShowLevelPagingTail` — вычисляемое в `ClayTreeNodeView`:
`!Node.LoadedAllChildren && Tree is ClayTreeView { Options.LevelPageSize: > 0 }`.
(Режим NestedSet гарантирован тем, что в ParentKey `LoadedAllChildren` всегда `true` из CTP1 —
дополнительной проверки режима в UI не нужно, но убедиться.)

Отступ хвоста — по уровню **детей** (`(Node.Level + 1) * IndentPx` или внутри контейнера
`clay-tree-children`, если включены линии CTF6), чтобы кнопка/индикатор стояли на месте
непоказанных детей, а не у родителя.

## Режим Button

```razor
@if (paging == ClayTreeLevelPagingMode.Button)
{
    <div class="clay-tree-level-more" style="padding-left:@paddingPx px">
        @if (Node.IsLoading)
        {
            <MudProgressCircular Color="Color.Primary" Indeterminate="true" Size="Size.Small" />
        }
        else
        {
            <MudButton Variant="Variant.Text" Size="Size.Small" OnClick="HandleLoadMore">
                @($"Загрузить ещё {Tree.LevelPageSize}")
            </MudButton>
        }
    </div>
}
```

`HandleLoadMore` → `view.LoadMoreChildrenAsync(Node)` (по образцу `HandleToggle`). Текст —
константой в `ClayTreeStrings` (или где лежат строки дерева), не хардкодом. `Tree.LevelPageSize`
— пробросить через `IClayTreeView`/`ClayTreeView` доступ к `Options.LevelPageSize`, как уже
проброшен `IndentPx`.

Во время загрузки кнопка заменяется спиннером (тем же `Size.Small`), повторный клик исключён
(`IsLoading` в `LoadMoreChildrenAsync` и так отсекает — CTP1).

## Режим Scroll

Сентинел вместо кнопки; при попадании в вьюпорт — автозагрузка через `IntersectionObserver`.

```razor
@if (paging == ClayTreeLevelPagingMode.Scroll)
{
    <div class="clay-tree-level-sentinel" @ref="_sentinel" style="padding-left:@paddingPx px">
        @if (Node.IsLoading)
        {
            <MudProgressCircular Color="Color.Primary" Indeterminate="true" Size="Size.Small" />
        }
    </div>
}
```

JS: файл `wwwroot/js/clay-tree-paging.js` (или дописать в существующий tree-js, если он есть),
экспортирует `observe(element, dotNetRef)` и `unobserve(element)`. При пересечении сентинела
с вьюпортом → `dotNetRef.invokeMethodAsync("OnSentinelVisible")`.

В `ClayTreeNodeView.razor.cs`:
- `ElementReference _sentinel;` `DotNetObjectReference<ClayTreeNodeView>? _selfRef;`
- в `OnAfterRenderAsync`: если режим Scroll, есть ещё данные и сентинел отрендерен и ещё не
  наблюдается — создать `_selfRef` и вызвать `observe`; когда `LoadedAllChildren` стало `true`
  — `unobserve`;
- `[JSInvokable] public async Task OnSentinelVisible()` → `LoadMoreChildrenAsync(Node)`;
- `IDisposable`/`IAsyncDisposable`: `unobserve` + `_selfRef?.Dispose()`.

**Загрузка идёт через тот же `LoadMoreChildrenAsync`** — Scroll и Button отличаются только тем,
кто его дёрнул.

## Ловушки

1. **Каскад автоподгрузки.** Если сентинел так и остаётся в вьюпорте после загрузки (порция
   мелкая, всё влезло) — `IntersectionObserver` дёрнет снова, и уровень «прокрутится» весь
   автоматически. Это приемлемо (данных мало — грузим всё), но не должно **зациклиться** при
   `LoadedAllChildren=true`: после дочитывания сентинел убирается из DOM (`ShowLevelPagingTail`
   становится false) и `unobserve` вызывается. Проверить, что наблюдение снимается.
2. **Повторное наблюдение.** `OnAfterRenderAsync` вызывается многократно — не создавать
   `observe` на каждый рендер. Флаг `_observing`, ставить/снимать аккуратно.
3. **`IntersectionObserver` root.** Дерево может быть в скролл-контейнере, а не во вьюпорте
   страницы. Если у дерева свой контейнер прокрутки — передать его как `root` наблюдателя,
   иначе сентинел «виден» не тогда. Определить по вёрстке дерева; если контейнера нет — `root:
   null` (вьюпорт).
4. **Утечка `DotNetObjectReference`.** Обязательно `Dispose` в `IDisposable` узла и `unobserve`,
   иначе на большом дереве — россыпь живых ссылок. Blazor Server особенно чувствителен.
5. **StyleGuard.** `clay-tree-level-more`/`clay-tree-level-sentinel` — структурные стили
   (`padding-left`, высота сентинела) inline допустимы; цвет спиннера — палитра. Класс в
   `clay.css`, если нужен фон/hover.
6. **Первичная загрузка vs догрузка.** Спиннер догрузки — в хвосте уровня, а не на узле
   (per-node спиннер из CTF5 — про разворачивание самого узла). Не перепутать: это два разных
   индикатора.

## Не делай

- Не дублируй загрузку — Button и Scroll зовут один `LoadMoreChildrenAsync` из CTP1.
- Не рисуй хвост для ParentKey и при `LevelPageSize=0` (`ShowLevelPagingTail` это отсекает
  через `LoadedAllChildren`).
- Не изобретай свой JS-подход, если в библиотеке уже есть интероп-файл — повтори его стиль
  подключения и оформления.
- Не пагинируй корневой уровень (CTP1 его не пагинирует — UI-хвост к корням не рисовать).
- Не забудь `Dispose` наблюдателя.

## Проверка

**Режим Button** (`/tree-test`, NestedSet, `LevelPageSize=N`, узел с >N детьми,
`LevelPagingMode=Button`):
- под первой порцией — кнопка «Загрузить ещё N»;
- клик → спиннер → дописались следующие N, кнопка снова внизу; один запрос на клик;
- уровень дочитан → кнопка исчезла;
- свернуть/развернуть → загруженные порции на месте, кнопка в актуальном состоянии.

**Режим Scroll** (`LevelPagingMode=Scroll`):
- доскролл до конца порции → автозагрузка следующей, без клика;
- индикатор в хвосте на время загрузки;
- уровень дочитан → сентинел исчез, дальнейший скролл ничего не грузит (в консоли нет
  бесконечных `invokeMethodAsync`);
- быстрый скролл не ломает порядок и не задваивает порции.

**Общее:**
- `LevelPageSize=0` / ParentKey → хвоста нет ни в одном режиме;
- нет утечек: раскрыть-свернуть много узлов, в консоли нет ошибок про disposed refs;
- тёмная тема; спиннер догрузки не путается со спиннером узла (CTF5);
- `dotnet build` + `dotnet test` — зелёные.
