> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md** и **GG0_README_dynamic_grouping.md**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB13 — при раскрытии группы прокрутка прыгает в начало страницы

Прочитать перед началом: `Components/Grid/ClayGrid.razor` — тег `<MudDataGrid … @key="_dataKey"
FixedHeader="true" Height="@_gridHeight">` (скролл-контейнер — внутренний контейнер MudDataGrid
из-за `FixedHeader`+`Height`); `Components/Grid/ClayGrid.razor.cs` — поле `_dataKey`, все места
`_dataKey++`; `Components/Grid/ClayGrid.Dynamic.Grouping.cs` — `ToggleDynamicGroup`;
`Components/Grid/ClayGrid.Grouping.cs` — `HandleGroupToggle`, `ToggleGroup` (статика);
`Components/Grid/ClayGrid.Dynamic.cs` — `NotifyQueryChanged`/`LoadDynamicData` и `_dataKey++`
на строке ~569; `wwwroot/js/` — есть ли уже helper для скролла (нет — заведём новый).

## Дефект

`MudDataGrid` помечен `@key="_dataKey"`. `@key` для Blazor — тождество элемента: при смене
значения ключа фреймворк **выбрасывает старый DOM-узел компонента и создаёт новый**. У нового
скролл сброшен в 0. `_dataKey` инкрементится в `NotifyQueryChanged` (каждая перезагрузка данных),
а раскрытие/сворачивание группы идёт именно через `NotifyQueryChanged`. Итог: раскрыли группу
в середине длинной страницы — грид пересоздался, прокрутка ушла в начало, пользователь потерял
место.

Зачем вообще `@key="_dataKey"`: пересборка нужна, чтобы `MudDataGrid` гарантированно перестроил
ячейки (например, после смены таймзоны — `ClayGrid.Dynamic.cs:70`, или порядка колонок). Но для
раскрытия группы полная пересборка избыточна — меняется только набор строк, а `Items` и так
новый. То есть проблема — в применении «тяжёлого» инструмента (`@key`) к «лёгкой» операции.

Лечим не отменой `@key` (она нужна другим сценариям), а **сохранением и восстановлением позиции
прокрутки** вокруг тех перезагрузок, где пересборка нежелательна. Это надёжнее, чем выборочно
не трогать `_dataKey`: даже без `@key` MudDataGrid при полной смене `Items` может сдвинуть
скролл, а явное восстановление позиции покрывает оба случая.

## Изменить/создать

### 1. JS-хелпер сохранения/восстановления прокрутки

Новый файл `wwwroot/js/clayGridScroll.js`:

```js
// Сохранение и восстановление позиции прокрутки тела грида вокруг перезагрузки строк.
// FixedHeader+Height у MudDataGrid делают скроллящимся внутренний .mud-table-container.
window.clayGridScroll = (function () {
    function container(gridId) {
        var root = document.getElementById(gridId);
        return root ? root.querySelector('.mud-table-container') : null;
    }
    return {
        capture: function (gridId) {
            var c = container(gridId);
            return c ? c.scrollTop : 0;
        },
        // Восстанавливаем после того, как новый DOM отрисован. requestAnimationFrame
        // ждёт следующего кадра — к этому моменту MudDataGrid уже перестроил тело.
        restore: function (gridId, top) {
            if (!top) return;
            requestAnimationFrame(function () {
                var c = container(gridId);
                if (c) c.scrollTop = top;
            });
        }
    };
})();
```

Селектор `.mud-table-container` — фактический скролл-контейнер MudDataGrid при
`FixedHeader`+`Height`. **Проверь его в DevTools** на реально отрендеренном гриде: у используемой
версии MudBlazor класс может отличаться. Если отличается — поправь в одном месте (`container`),
не подбирай наугад.

Подключить `clayGridScroll.js` в `App.razor` ОБОИХ приложений рядом с прочими
`_content/Clayzor.Lib.Web.Controls/js/…` (файл раздаётся как static web asset RCL, доп.
настройка csproj не нужна).

### 2. `ClayGrid.Dynamic.Grouping.cs` — восстановить прокрутку вокруг toggle

`ToggleDynamicGroup` сохраняет позицию до перезагрузки и возвращает после:

```csharp
private async Task ToggleDynamicGroup(GroupHeaderRow header)
{
    var scrollTop = await JS.InvokeAsync<double>("clayGridScroll.capture", Id);

    var wasExpanded = _dynamicExpandedGroups.Contains(header.FullKey);
    if (wasExpanded) _dynamicExpandedGroups.Remove(header.FullKey);
    else             _dynamicExpandedGroups.Add(header.FullKey);

    await NotifyQueryChanged();

    // …существующая логика автоперехода страницы при раскрытии последней группы
    //    и коррекции _pageNumber при сворачивании — без изменений…

    await InvokeAsync(StateHasChanged);

    // Автопереход на другую страницу — это осмысленная смена контекста, прокрутку
    // туда возвращать не нужно; в остальных случаях сохраняем позицию.
    await JS.InvokeVoidAsync("clayGridScroll.restore", Id, scrollTop);
}
```

Тонкость: если внутри сработал `_pageNumber++`/`_pageNumber = _totalPages` (переход на другую
страницу), восстанавливать старую позицию не надо — пользователь уехал в другой контент.
Заведи локальный флаг `pageChanged` в существующих ветках автоперехода и вызывай `restore`
только когда `!pageChanged` (иначе передавай `0`).

### 3. Статический режим — та же защита

Раскрытие группы в статике идёт через `HandleGroupToggle` → `OnGroupToggle` (страница). Точка,
где грид перезагружается и `_dataKey` растёт, — в `NotifyQueryChanged`. Чтобы не расставлять
capture/restore по обоим путям, оберни ими сам `HandleGroupToggle` в `ClayGrid.Grouping.cs`
(он общий вход для клика по заголовку группы в обоих режимах — проверь по коду; если общий,
capture/restore ставится один раз здесь и п. 2 сводится к учёту `pageChanged`).

Если общей точки нет и статический toggle целиком уходит в страницу — добавь capture/restore
в `HandleGroupToggle` вокруг `OnGroupToggle.InvokeAsync(header)` симметрично п. 2.

## Не делай

- Не убирай `@key="_dataKey"` и не переставай инкрементить `_dataKey` в других местах
  (таймзона, порядок колонок, настройка колонок) — там пересборка нужна намеренно. Правка —
  восстановление скролла, а не отмена пересборки.
- Не сохраняй/не восстанавливай прокрутку при смене страницы, сортировке, фильтрации, смене
  группировки — там уход в начало ожидаем и правилен. Только раскрытие/сворачивание группы.
- Не восстанавливай позицию через `scrollIntoView` конкретной строки — строки после
  перезагрузки другие (добавились детали группы), якорь ненадёжен. Восстанавливаем `scrollTop`
  контейнера.
- Не читай/не пиши `scrollTop` синхронно сразу после `StateHasChanged` без
  `requestAnimationFrame` — до кадра новый DOM ещё не построен, восстановление не сработает.
- Не заводи хранение позиции в C#-поле между рендерами как «умный» кеш — capture прямо перед
  операцией, restore сразу после, локальной переменной достаточно.

## Проверка (ручная)

- `Kesco.App.Web.Inventory`, `?id=140`, группировка ВКЛ, страница с числом строк больше высоты
  грида: прокрутить к группе в середине/внизу → раскрыть её → прокрутка осталась на этой группе,
  НЕ прыгнула в начало; видны раскрывшиеся под ней строки;
- свернуть ту же группу → прокрутка на месте;
- раскрыть последнюю группу страницы, детали которой не влезли (срабатывает автопереход на
  след. страницу) → грид на новой странице показан с начала (прокрутку назад не тащим);
- вложенная группировка: раскрыть подгруппу второго уровня в середине → позиция сохранена;
- сортировка / смена страницы / применение фильтра → грид уходит в начало (как и должно);
- статический режим (`/medical-tests`): раскрытие группы в середине длинной страницы → позиция
  сохранена; смена страницы → в начало;
- медленный канал/большой объём: во время загрузки (оверлей GB12) восстановление срабатывает
  после появления строк, без «двойного прыжка»;
- DevTools → у скролл-контейнера действительно класс, указанный в `clayGridScroll.js`;
- `dotnet build` + `dotnet test` — зелёные.
