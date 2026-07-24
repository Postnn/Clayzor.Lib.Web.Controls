> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md** и **STYLE_RULES.md** (§2 запрещённые инлайны, §4 классы в app.css). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB4 — диалог настройки колонок: шапка не над своими колонками, чипы видны в щели

Прочитать перед началом: `Components/Grid/ClayColumnSettingsDialog.razor` — целиком (разметка
шапки, разметка чипа, `ShowSorting`, `ShowGrouping`, `GetDialogSortBadge`, `GetDialogGroupBadge`);
`Components/Grid/ColumnSettingsItem.cs`; `Components/Grid/ClayGrid.razor.cs` —
`OpenColumnSettings` (какие `DialogOptionsEx` передаются); `Components/Grid/ClayGrid.ExportMenu.cs`
— `ResolveExportColumnsAsync` (второй вызов того же диалога, `ShowSorting=false`);
`wwwroot/js/clayColumnSettings.js` — целиком (особенно `createGhost`, `movePlaceholder`,
`getChips`, `getTargetIdx`, `startDrag`); `app.css` — **обе копии**, блоки
`.column-settings-chip`, `.column-settings-ghost`, `.column-settings-placeholder`,
`.clay-column-settings-header`, `.clay-column-settings-row`, `.clay-column-settings-label`,
`.chip-sort-badge`, `.chip-group-badge`.

## Дефект

**1. Шапка не совпадает с колонками.** Шапка и строки свёрстаны РАЗНЫМИ flex-раскладками с
разными числами. Шапка:

```razor
<div style="width:38px;padding-right:50px;display:flex;justify-content:center">   @* Группировка *@
<div style="width:50px;display:flex;justify-content:flex-start;padding-left:6px"> @* Видимость *@
<div style="width:38px;display:flex;justify-content:center">                      @* Фильтр *@
```

Строка:

```razor
<div style="display:flex;justify-content:center;align-items:center;gap:2px;min-width:38px">  @* MudSwitch + бейдж *@
<div @onpointerdown:stopPropagation="true" style="display:flex;align-items:center;gap:6px">  @* два MudSwitch подряд *@
```

`MudSwitch` шириной ~58px в контейнере с `min-width:38px` растягивает свою ячейку, а в шапке
на том же месте стоит жёсткие `38px` с `padding-right:50px`. Совпадения нет и быть не может:
у шапки и строк нет общего источника ширин. Плюс два переключателя строки («Видимость» и
«Фильтр по значению») лежат в ОДНОМ `div`, а в шапке им соответствуют ДВА разных.

**2. Чипы в щели.** `.clay-column-settings-header` — `position: sticky; top: 0`, но живёт внутри
`.mud-dialog-content`, у которого свои 24px паддинга. Sticky-смещение отсчитывается от границы
padding-box скроллпорта, поэтому над прилипшей шапкой остаётся прозрачная полоса в высоту
`padding-top`, и сквозь неё видно проезжающие чипы. Костыли `margin-bottom: -4px` (в копии
Kesco) / `2px` (в копии MedicalTests) борются с другим симптомом — просветом между шапкой и
первым чипом из-за `gap:4px` контейнера — и первопричину не трогают.

Лечится не подбором отступов, а конструкцией: **одна сетка на шапку и на строки** и
**нулевой паддинг у контента диалога**.

## Изменить/создать

### 1. `ClayColumnSettingsDialog.razor` — контент без паддинга, список без gap

```razor
<MudDialog ContentClass="clay-column-settings-content pa-0">
    <DialogContent>
        <div @ref="_container"
             class="@ListClass">
```

`ContentClass` — параметр `MudDialog` (MudBlazor 9). Если имя параметра в текущей версии другое —
найди фактическое в исходниках пакета и используй его; **не** протаскивай паддинг через
`DialogOptionsEx` и не правь глобально `.mud-dialog-content` — сломаешь остальные диалоги.

```csharp
/// <summary>
/// Классы контейнера списка. Модификаторы включают колонки сетки под группировку
/// и фильтр по значению — ширины задаются в app.css через --clay-cs-*.
/// </summary>
private string ListClass =>
    "clay-column-settings-list"
    + (ShowGrouping ? " clay-column-settings-list--grouping" : "")
    + (ShowSorting  ? " clay-column-settings-list--filter"   : "");
```

Инлайновые `class="d-flex flex-column" style="gap:4px"` с контейнера убрать — раскладка теперь
в `.clay-column-settings-list`.

### 2. Единая сетка: шапка и строка — одинаковый набор ячеек

Ячеек ВСЕГДА пять и всегда в одном порядке: `ручка | название | группировка | видимость | фильтр`.
Ячейки группировки и фильтра рендерятся всегда, даже когда режим выключен, — тогда их колонка
имеет нулевую ширину (`--clay-cs-*` не задан). Так число grid-элементов в шапке и в строке
совпадает при любых `ShowGrouping`/`ShowSorting`, и выравнивание получается по построению,
а не подбором пикселей.

Шапка:

```razor
<div class="column-settings-chip clay-column-settings-header">
    <div class="clay-cs-cell clay-cs-cell--handle"></div>
    <div class="clay-cs-cell clay-cs-cell--label"><span>Колонка</span></div>
    <div class="clay-cs-cell">
        @if (ShowGrouping)
        {
            <MudTooltip Text="Группировка" Placement="Placement.Top">
                <MudIcon Icon="@Icons.Material.Filled.AccountTree" Size="Size.Small" />
            </MudTooltip>
        }
    </div>
    <div class="clay-cs-cell">
        <MudTooltip Text="Видимость" Placement="Placement.Top">
            <MudIcon Icon="@Icons.Material.Filled.Visibility" Size="Size.Small" />
        </MudTooltip>
    </div>
    <div class="clay-cs-cell">
        @if (ShowSorting)
        {
            <MudTooltip Text="Фильтр по значению" Placement="Placement.Top">
                <MudIcon Icon="@Icons.Material.Filled.Checklist" Size="Size.Small" />
            </MudTooltip>
        }
    </div>
</div>
```

Строка — тот же скелет; содержимое ячеек берётся из текущей разметки без изменения логики
(`sort-toggle-area` с `@onclick="() => ToggleDialogSort(item.SqlName)"` и `@onclick:stopPropagation`,
бейджи `GetDialogSortBadge`/`GetDialogGroupBadge`, `MudSwitch` видимости с `Disabled="@item.IsReadonly"`,
`MudSwitch` `AllowValueFilter`, `@onpointerdown:stopPropagation="true"` на ячейках с
переключателями, `data-col-idx="@idx"` на самом чипе):

```razor
<div class="column-settings-chip" data-col-idx="@idx">
    <div class="clay-cs-cell clay-cs-cell--handle">
        <MudIcon Icon="@Icons.Material.Filled.DragIndicator" Size="Size.Small" />
    </div>
    <div class="clay-cs-cell clay-cs-cell--label"> … название + бейдж сортировки … </div>
    <div class="clay-cs-cell" @onpointerdown:stopPropagation="true"> … MudSwitch группировки + бейдж … </div>
    <div class="clay-cs-cell" @onpointerdown:stopPropagation="true"> … MudSwitch видимости … </div>
    <div class="clay-cs-cell" @onpointerdown:stopPropagation="true"> … MudSwitch фильтра … </div>
</div>
```

Опасность: непрозрачность (`opacity`), курсор (`cursor:grab`) и прочие структурные инлайны
с иконок можно оставить — StyleGuard их пропускает. Всё, что цвет/фон/граница/шрифт — только
в `app.css`.

### 3. `app.css` — **в ОБЕИХ копиях одинаково**

```css
/* ── Column settings dialog: контент без паддинга — иначе sticky-шапка прилипает
      к границе padding-box и над ней остаётся прозрачная полоса, сквозь которую
      видно проезжающие чипы ── */
.clay-column-settings-content {
    padding: 0 !important;
}

/* Список чипов. Без gap: разделитель — border-bottom самого чипа,
   иначе между шапкой и первым чипом просвет (старый костыль margin-bottom: -4px). */
.clay-column-settings-list {
    display: flex;
    flex-direction: column;
    --clay-cs-group-w: 0px;
    --clay-cs-filter-w: 0px;
    --clay-cs-quicksearch-w: 0px;
}
.clay-column-settings-list--grouping    { --clay-cs-group-w: 88px; }
.clay-column-settings-list--filter      { --clay-cs-filter-w: 64px; }
.clay-column-settings-list--quicksearch { --clay-cs-quicksearch-w: 64px; }

/* Сетка чипа. Одна и та же для шапки и для строк — выравнивание по построению.
   Шесть колонок: ручка | название | группировка | видимость | фильтр | быстрый поиск.
   Колонки 3,5,6 схлопываются в 0px когда соответствующий режим выключен. */
.column-settings-chip {
    display: grid;
    grid-template-columns:
        24px
        minmax(0, 1fr)
        var(--clay-cs-group-w)
        64px
        var(--clay-cs-filter-w)
        var(--clay-cs-quicksearch-w);
    align-items: center;
    /* остальное (background, color, min-height, padding, border-*, cursor, transition)
       — как было, кроме display:flex */
}

.clay-cs-cell {
    display: flex;
    align-items: center;
    justify-content: center;
    min-width: 0;
    overflow: hidden;
}
.clay-cs-cell--label {
    justify-content: flex-start;
    gap: 3px;
    white-space: nowrap;
    text-overflow: ellipsis;
}
.clay-cs-cell--handle { justify-content: flex-start; }

/* MudSwitch несёт собственные отступы — в ячейке фиксированной ширины они режут трек */
.column-settings-chip .mud-switch { margin: 0; }
```

Из `.clay-column-settings-header` убрать `margin-bottom` (в обеих копиях: `-4px` и `2px`).
`position: sticky; top: 0; z-index: 1` — оставить, теперь он работает.
`.clay-column-settings-row` и `.clay-column-settings-label` после правки осиротеют — проверь
grep-ом по решению и удали, если ссылок не осталось.

Ширины `24px / 88px / 64px / 64px` — **стартовые**. Проверь фактический рендер `MudSwitch` и
бейджа и подгони так, чтобы трек не резался и бейдж помещался; числа держи только в `app.css`.

### 4. `clayColumnSettings.js` — ghost должен унаследовать сетку

Ghost — клон чипа, который вешается в `document.body`. Переменные `--clay-cs-*` объявлены на
`.clay-column-settings-list`, до `body` не доедут → у ghost колонки схлопнутся и он поедет
визуально. В `createGhost` после `cloneNode` скопировать их с контейнера:

```js
var cs = getComputedStyle(container);
g.style.setProperty('--clay-cs-group-w',       cs.getPropertyValue('--clay-cs-group-w'));
g.style.setProperty('--clay-cs-filter-w',      cs.getPropertyValue('--clay-cs-filter-w'));
g.style.setProperty('--clay-cs-quicksearch-w', cs.getPropertyValue('--clay-cs-quicksearch-w'));
```

Остальное в JS не трогать: контейнер остаётся flex-колонкой, placeholder остаётся обычным
`div` (grid-элементом он не становится — сетка живёт на чипе, а не на списке), шапка по-прежнему
без `data-col-idx` и в `getChips()`/`getTargetIdx()` не попадает.

### 5. Ширина диалога

`ClayGrid.razor.cs`, `OpenColumnSettings` и `ClayGrid.ExportMenu.cs`, `ResolveExportColumnsAsync`
(вызов `ClayColumnSettingsDialog`, НЕ `ClayColumnSettingsPromptDialog`): `MaxWidth.ExtraSmall`
(444px) на пять колонок — источник давки; поставить `MaxWidth = MaxWidth.Small`.
`FullWidth`, `DragMode` — без изменений.

### 6. `STYLE_RULES.md` §4

Строка в таблицу: «Список колонок в диалоге настройки → `.clay-column-settings-list` +
`.column-settings-chip` + `.clay-cs-cell`». Больше в документации ничего не менять.

## Не делай

- Не правь глобально `.mud-dialog-content` — паддинг убирается только у этого диалога,
  через класс на `ContentClass`.
- Не используй `subgrid` (поддержка браузеров у заказчика не зафиксирована) и не вешай
  `display: contents` на чипы — они несут фон, hover и border, им нужен свой бокс.
- Не убирай ячейки группировки/фильтра при выключенных `ShowGrouping`/`ShowSorting` — сетка
  разъедется. Ячейка остаётся, колонка получает нулевую ширину.
- Не трогай логику `ToggleDialogSort` / `ToggleDialogGroup` / `_syncSortToItems` /
  `_syncGroupToItems` / `Apply` / `ResetAll` — дефект чисто вёрсточный.
- Не переписывай drag-and-drop на HTML5 DnD или на внешнюю библиотеку. Из JS меняется ровно
  `createGhost`.
- Не трогай `ClayColumnSettingsPromptDialog` (маленький диалог «Выбрать колонки / Как на
  странице / Отмена») — к нему претензий нет.
- Кнопки `DialogActions` — **не в этом шаге**, это GB5.

## Проверка (ручная)

- `/medical-tests` → «Настройка колонок»: подписи шапки («Колонка», иконки «Группировка»,
  «Видимость», «Фильтр по значению») стоят ровно над своими переключателями во всех строках;
- перетащить границу окна браузера / уменьшить ширину до ~600px → колонки управления сохраняют
  ширину, съезжает только название (ellipsis), наложений нет;
- длинное название колонки → многоточие, переключатели на месте;
- прокрутить список: чипы уходят ПОД шапку, между шапкой и заголовком диалога щели нет,
  просвета между шапкой и первым чипом нет;
- перетащить чип: ghost выглядит как строка (сетка не схлопнулась), placeholder на месте,
  авто-прокрутка у краёв работает, порядок после «Применить» — как перетащили;
- шапку перетащить нельзя;
- клик по названию колонки → сортировка ASC → DESC → нет; бейдж `1↑`/`2↓` на месте;
  клик по переключателю не запускает перетаскивание;
- включить группировку (трей открыт) → колонка «Группировка» появилась, ширина строк
  пересчиталась, шапка совпала; выключить трей → колонка исчезла, сетка совпадает;
- диалог из печати/экспорта (`ShowSorting=false`): колонок только три (ручка, название,
  видимость), шапка совпадает, бейджей и кнопки сброса сортировки нет;
- `Kesco.App.Web.Inventory`, `?id=140` → тот же диалог, те же проверки (CSS правился в обеих
  копиях `app.css`);
- тёмная тема → фон шапки и чипов читаем;
- `dotnet build` зелёный (StyleGuard).
