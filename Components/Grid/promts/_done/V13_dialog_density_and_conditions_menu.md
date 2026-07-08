# V13. Уплотнение диалога значения + список условий как меню справа

Две правки в `Components/Grid/ClayColumnValueFilterDialog.razor`:
1. Список условий («Текстовые/Числовые/… фильтры») открывать как **выпадающее
   меню справа от заголовка** (оверлей), а не `MudExpansionPanel`, который
   раздвигает контент и сдвигает список значений вниз.
2. Уплотнить диалог (меньше отступы, плотнее строки).

Behavior-preserving: набор операторов и `OpenCondition(op)` не меняются.

## 1. Условия — right-anchored `MudMenu` вместо `MudExpansionPanel`
Заменить блок `@if (_availableOperators.Count > 1 && ColumnType != ColumnType.Boolean) { <MudExpansionPanel …> … </MudExpansionPanel> }`
на меню с текстовым активатором и якорем справа (попап — оверлей, значения не
сдвигает):
> ⚠️ В этой версии MudBlazor `MudMenu` с `ActivatorContent` **не вешает клик сам** —
> активатор обязан вызвать `ToggleAsync` из контекста (см. рабочую обёртку
> `Components/ClayMenu.razor`: `<ActivatorContent Context="x">` →
> `((dynamic)x).ToggleAsync(null)`). Без этого меню не открывается.

```razor
@if (_availableOperators.Count > 1 && ColumnType != ColumnType.Boolean)
{
    <div style="display:flex;align-items:center">
        <MudMenu Dense="true"
                 AnchorOrigin="Origin.TopRight"
                 TransformOrigin="Origin.TopLeft">
            <ActivatorContent Context="menuCtx">
                @{ var ctx = (object)menuCtx; }
                <div style="display:flex;align-items:center;gap:4px;cursor:pointer;padding:2px 0"
                     @onclick="@(async () => await ((dynamic)ctx).ToggleAsync(null))">
                    <MudText Typo="Typo.body2" Style="font-weight:600">@_conditionsLabel</MudText>
                    <MudIcon Icon="@Icons.Material.Filled.ChevronRight" Size="Size.Small" />
                </div>
            </ActivatorContent>
            <ChildContent>
                @foreach (var op in _availableOperators)
                {
                    if (!_descriptor.OperatorTakesValue(op) && op != ColumnFilterOperator.IsNull
                        && op != ColumnFilterOperator.IsNotNull && op != ColumnFilterOperator.IsEmpty
                        && op != ColumnFilterOperator.IsNotEmpty)
                        continue;

                    var captured = op;
                    <MudMenuItem Dense="true" OnClick="() => OpenCondition(captured)">
                        @ClayFilterOperatorLabels.Get(op)
                    </MudMenuItem>
                }
            </ChildContent>
        </MudMenu>
    </div>
}
```
Ключевое:
- активатор вызывает `((dynamic)ctx).ToggleAsync(null)` по `@onclick` — иначе меню
  не раскроется (как в `ClayMenu`);
- `AnchorOrigin="Origin.TopRight"` + `TransformOrigin="Origin.TopLeft"` — меню
  раскрывается вправо от заголовка; попап поверх контента, значения не сдвигаются;
- `captured` — фикс замыкания в цикле.

## 2. Уплотнение
### Класс на корень контента
Строка `<div style="display:flex;flex-direction:column;gap:12px;min-width:320px">`
→ добавить класс и уменьшить зазор/ширину:
```razor
<div class="clay-vf-dialog" style="display:flex;flex-direction:column;gap:6px;min-width:300px">
```

### Стили (заменить блок `<style>` в этом файле)
Скопить строки и ужать отступы. Правила скоупить под `.clay-vf-dialog`, чтобы не
задевать другие диалоги:
```razor
<style>
    .clay-vf-dialog { font-size: .875rem; }
    /* компактные строки чекбоксов (ClayCheckbox после V11 или span-строки после V10) */
    .clay-vf-dialog .clay-checkbox { padding: 1px 0 !important; gap: 6px; }
    /* длинные подписи — в одну строку с многоточием */
    .clay-vf-dialog .clay-checkbox > span:last-child {
        white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 340px;
    }
    /* прокрутка значений — фикс. высота, чуть ниже */
    .clay-vf-dialog .value-filter-values-scroll { overflow-y: auto; max-height: 260px; }
    .clay-vf-dialog .value-filter-select-all {
        position: sticky; top: 0; z-index: 1;
        background: var(--mud-palette-background);
        padding-bottom: 2px; border-bottom: 1px solid var(--mud-palette-divider);
    }
    /* уплотнить меню операторов */
    .mud-popover .mud-list { max-height: 260px; }
    .mud-popover .mud-list-item { min-height: 30px; padding-block: 3px; }
    /* поджать паддинги самого диалога */
    .clay-vf-dialog { }
</style>
```
Если после V11 чекбоксы — это `ClayCheckbox` (класс `.clay-checkbox`), правила
выше подходят. Если ещё span-строки из V10 — они тоже внутри `.clay-checkbox`? Нет:
в V10 у строк был свой inline-стиль. Тогда в V10-разметке добавить класс
`clay-checkbox` на строку-обёртку `<div>`, либо снизить inline `padding:4px 0`
до `padding:1px 0`. Рекомендуется сначала выполнить V11 (компонент), тогда
селектор `.clay-checkbox` работает единообразно.

### Кнопки/заголовок (опционально)
Уплотнить `<DialogActions>`: у `MudButton` `Size="Size.Small"`. Отступы заголовка
и actions MudDialog можно поджать точечно (не обязательно для задачи).

## Проверка
- [ ] Клик по «…фильтры» открывает меню (активатор вызывает ToggleAsync) — список условий как меню **справа** от
      заголовка; список значений при этом не сдвигается.
- [ ] Выбор условия из меню открывает форму условия (как раньше, через
      `OpenCondition`/`OpenConditionRequest`).
- [ ] Диалог заметно компактнее: меньше вертикальные зазоры, плотные строки,
      длинные значения — с многоточием; прокрутка значений сохранена.
- [ ] Правила уплотнения не протекают на другие диалоги (скоуп `.clay-vf-dialog`).
- [ ] `dotnet build` без ошибок.
