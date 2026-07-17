> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md** и **STYLE_RULES.md** (§2, §4). Требует выполненного **GB4**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB5 — диалог настройки колонок: кнопки наезжают друг на друга при узкой ширине

Прочитать перед началом: `Components/Grid/ClayColumnSettingsDialog.razor` — блок
`<DialogActions>` целиком; `Components/Grid/ClayGrid.razor.cs` — `OpenColumnSettings`
(`DialogOptionsEx`, после GB4 там `MaxWidth.Small`); MudBlazor — правила
`.mud-dialog-actions` (`display: flex`, `justify-content: flex-end`, отступ между потомками);
`app.css` — обе копии, блок `/* ── Column settings dialog ── */`.

## Дефект

```razor
<DialogActions>
    <div style="flex:1;display:flex;gap:4px">      @* сброс сортировки / группировки / всего *@
        …три MudIconButton в MudTooltip…
    </div>
    <div style="display:flex;gap:4px">             @* Отмена / Применить *@
        <MudButton OnClick="Cancel">Отмена</MudButton>
        <MudButton Color="Color.Primary" OnClick="Apply">Применить</MudButton>
    </div>
</DialogActions>
```

Три проблемы разом:

1. `.mud-dialog-actions` — flex-строка **без `flex-wrap`**. Перенос невозможен в принципе:
   при нехватке места элементы только сжимаются.
2. Левый блок — `flex:1` (то есть `flex-grow:1; flex-shrink:1; flex-basis:0`), правый — без
   `flex-shrink:0`. При сужении сжимаются ОБА, причём у правого внутри `MudButton` с
   `min-width` и текстом → кнопки лезут друг на друга и на кнопки сброса.
3. У блоков нет `min-width: 0`, а MudBlazor добавляет `.mud-dialog-actions > * + *`
   собственный отступ — итоговая ширина оказывается больше суммы видимых элементов.

`MudTooltip` вокруг `MudIconButton` добавляет ещё один inline-обёрточный `span`, у которого
своих правил сжатия нет, — визуально кнопки сброса «слипаются» первыми.

Итог: главные кнопки («Отмена», «Применить») обязаны быть неразрывной группой и никогда не
сжиматься; кнопки сброса — уступать место и переноситься на строку выше при нехватке ширины.

## Изменить/создать

### 1. `ClayColumnSettingsDialog.razor`

```razor
<DialogActions>
    <div class="clay-column-settings-actions">
        <div class="clay-column-settings-actions__reset">
            @if (ShowSorting)
            {
                <MudTooltip Text="Сбросить сортировку" Placement="Placement.Top">
                    <MudIconButton Icon="@Icons.Material.Filled.ClearAll" Size="Size.Small" OnClick="ResetSort" />
                </MudTooltip>
            }
            @if (ShowGrouping)
            {
                <MudTooltip Text="Сбросить группировку" Placement="Placement.Top">
                    <MudIconButton Icon="@Icons.Material.Filled.LayersClear" Size="Size.Small" OnClick="ResetGroup" />
                </MudTooltip>
            }
            <MudTooltip Text="Восстановить порядок и видимость колонок по-умолчанию" Placement="Placement.Top">
                <MudIconButton Icon="@Icons.Material.Filled.RestartAlt" Size="Size.Small" OnClick="ResetAll" />
            </MudTooltip>
        </div>
        <div class="clay-column-settings-actions__main">
            <MudButton OnClick="Cancel">Отмена</MudButton>
            <MudButton Color="Color.Primary" OnClick="Apply">Применить</MudButton>
        </div>
    </div>
</DialogActions>
```

Изменение ровно одно: два `div` с инлайнами обёрнуты в один контейнер с классами. Состав
кнопок, условия `@if`, обработчики, тултипы, порядок — без изменений.

### 2. `app.css` — **в ОБЕИХ копиях одинаково**

```css
/* ── Column settings dialog: строка действий ──
   Обёртка нужна, потому что .mud-dialog-actions не переносит строки: при сужении
   диалога кнопки сжимались и наезжали друг на друга. Главная пара не сжимается
   никогда, кнопки сброса уступают место и переносятся. */
.clay-column-settings-actions {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px;
    width: 100%;
    min-width: 0;
}
.clay-column-settings-actions__reset {
    display: flex;
    align-items: center;
    gap: 4px;
    flex: 1 1 auto;
    min-width: 0;
}
.clay-column-settings-actions__main {
    display: flex;
    align-items: center;
    gap: 8px;
    flex: 0 0 auto;
    margin-left: auto;
}
```

Если после этого MudBlazor всё ещё вставляет собственный отступ между потомками
`.mud-dialog-actions` (`> * + *` или подобное) — снять его точечно, только для этого диалога:

```css
.clay-column-settings-actions-host.mud-dialog-actions > * { margin: 0; }
```

и повесить класс через `ActionsClass="clay-column-settings-actions-host"` на `<MudDialog>`
(параметр рядом с `ContentClass` из GB4). Если проблемы нет — правило не добавлять,
лишних сущностей не плодим.

## Не делай

- Не правь глобально `.mud-dialog-actions` — сломаешь `ConfirmDialog`, `ClayFilterDialog`,
  `ClayColumnFilterDialog`, `ClayColumnValueFilterDialog` и диалоги редактирования.
- Не прячь кнопки сброса на узкой ширине (`display:none`, «в меню ⋮») — функция должна
  оставаться доступной, задача — перенос, а не сокрытие.
- Не заменяй текстовые «Отмена»/«Применить» на иконки и не сокращай подписи.
- Не трогай `Apply` / `Cancel` / `ResetSort` / `ResetGroup` / `ResetAll` — дефект вёрсточный.
- Не добавляй `Resizeable`/`FullScreen` в `DialogOptionsEx` — это фича, а не фикс.
- `MaxWidth.Small` уже выставлен в GB4 — второй раз не трогай.

## Проверка (ручная)

- `/medical-tests` → «Настройка колонок» → медленно сузить окно браузера до ~400px:
  кнопки сброса и пара «Отмена/Применить» нигде не перекрываются; при нехватке места
  строка действий переносится, «Отмена» и «Применить» остаются рядом и не сжимаются;
- расширить обратно → кнопки сброса слева, «Отмена/Применить» прижаты вправо, как и было;
- то же на мобильной ширине (DevTools, 360px);
- тултипы кнопок сброса открываются и не «залипают» после клика;
- «Применить» при всех скрытых колонках → Snackbar «Должна быть видна хотя бы одна колонка»,
  диалог не закрылся (регрессия);
- диалог из печати/экспорта (`ShowSorting=false`, `ShowGrouping=false`): в блоке сброса одна
  кнопка `RestartAlt`, раскладка не поехала;
- диалог при включённой группировке (`ShowGrouping=true`): три кнопки сброса, всё влезает;
- `ConfirmDialog`, `ClayFilterDialog`, `ClayColumnFilterDialog`, `ClayColumnValueFilterDialog`,
  `MedicalTestEditDialog` → кнопки внизу выглядят как до фикса (не задели глобальные стили);
- `Kesco.App.Web.Inventory`, `?id=140` → те же проверки (CSS правился в обеих копиях `app.css`);
- `dotnet build` зелёный (StyleGuard).
