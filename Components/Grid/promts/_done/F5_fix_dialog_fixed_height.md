# F5. Диалог настраиваемого фильтра: фиксированная высота + 3 зоны

Заменяет высотную часть F3/F4 (переключатель И/ИЛИ уже исправлен).
Файлы: `Components/Grid/Filter/ClayFilterDialog.razor`,
`Components/Grid/ClayGrid.Filtering.cs`.

## Почему предыдущие попытки уезжали
`max-height` — только потолок: контент всё равно растёт и толкает футер вниз.
`overflow:hidden` + `flex:1;min-height:0` работают как скролл-контейнер только если
у родителя **задана высота**, от которой отсчитывается остаток. Пока высота диалога
«по контенту», отсчитывать не от чего. Решение — **зафиксировать высоту диалога**.

## Что сделать

### 1. Опции диалога (`ClayGrid.Filtering.cs`, `OpenCompositeFilterDialog`)
Обычный центрированный диалог, без перетаскивания:
```csharp
var options = new DialogOptionsEx
{
    MaxWidth  = MaxWidth.Small,
    FullWidth = false,
    CloseOnEscapeKey = true,
    // DragMode НЕ задавать
};
```

### 2. Разметка `ClayFilterDialog.razor` — фиксированная высота + 3 зоны
```razor
<MudDialog Class="clay-filter-dialog">
    <TitleContent>
        <MudText Typo="Typo.h6" Style="display:flex;align-items:center;gap:8px">
            <MudIcon Icon="@Icons.Material.Filled.FilterList" Size="Size.Small" />
            Настраиваемый фильтр
        </MudText>
    </TitleContent>

    <DialogContent>
        @* Фиксированная высота всей рабочей области; делится на 3 части *@
        <div style="display:flex;flex-direction:column;height:520px;width:560px;max-width:100%">

            @* (1) Описание — фиксировано сверху *@
            @if (!string.IsNullOrEmpty(_draftDescription))
            {
                <MudPaper Elevation="0" Class="pa-2 mb-2"
                          Style="flex:0 0 auto;background:var(--mud-palette-background-grey)">
                    <MudText Typo="Typo.body2">@_draftDescription</MudText>
                </MudPaper>
            }

            @* (2) Условия — единственная прокручиваемая зона *@
            <div style="flex:1 1 auto;overflow-y:auto;min-height:0">
                <ClayFilterGroup Node="@_draft" Columns="@Columns"
                                  LookupOptions="@LookupOptions" IsRoot="true"
                                  OnChanged="@OnDraftChanged" />
            </div>
        </div>
    </DialogContent>

    <DialogActions>
        <MudButton OnClick="@Reset" Variant="Variant.Text"
                   StartIcon="@Icons.Material.Filled.ClearAll"
                   Style="text-transform:none">Сбросить</MudButton>
        <MudSpacer />
        <MudButton OnClick="@Cancel" Variant="Variant.Text">Отмена</MudButton>
        <MudButton Color="Color.Primary" Variant="Variant.Filled"
                   OnClick="@Apply">Применить</MudButton>
    </DialogActions>
</MudDialog>

<style>
    /* scoped — только этот диалог */
    .clay-filter-dialog .mud-dialog-content { overflow: hidden; }
</style>
```

Ключевое:
- `height:520px` на рабочем контейнере — **фиксированная** высота (не max-height).
  От неё flex отсчитывает: описание `flex:0 0 auto`, зона условий `flex:1 1 auto` +
  `overflow-y:auto` + `min-height:0`.
- `.clay-filter-dialog .mud-dialog-content { overflow:hidden }` — чтобы скролл был
  только у зоны условий, а не у контента диалога. Стиль скоуплен.
- `DialogActions` вне `DialogContent` → всегда видны.
- Если 520px не подходит под ваши экраны — поставьте `min(520px, 72vh)` или другое
  значение; суть (фиксированная высота) сохраняется.

## Критерии
- [ ] Высота диалога постоянна независимо от числа условий.
- [ ] Прокручивается только список условий; описание и кнопки «Сбросить/Отмена/Применить» всегда видны.
- [ ] Диалог не нужно перетаскивать, чтобы добраться до кнопок.
- [ ] Никаких глобальных (нескоупленных) стилей.
- [ ] `dotnet build` без ошибок.

---

## Плотность/компактность (дополнение)

Сейчас условие занимает две строки (Поле/Условие + отдельная строка «Значение» во всю
ширину) и много паддингов — отсюда рыхлость и высокие карточки. Уплотнить:

### 1. `ClayFilterExpression.razor` — всё условие в одну строку
Свести Поле / Условие / Значение / ✕ в один ряд с переносом при нехватке ширины;
убрать отдельную строку значения:
```razor
<div style="display:flex;flex-wrap:wrap;gap:6px;align-items:center;
            padding:6px;border:1px solid var(--mud-palette-lines-default);border-radius:4px">

    <MudSelect T="string" Label="Поле" Value="@Node.Column" ValueChanged="@OnColumnChanged"
               Variant="Variant.Outlined" Margin="Margin.Dense" Dense="true"
               Style="flex:1 1 130px;min-width:120px">
        @foreach (var col in Columns)
        {
            <MudSelectItem Value="@col.SqlName">@col.DisplayName</MudSelectItem>
        }
    </MudSelect>

    <MudSelect T="ColumnFilterOperator" Label="Условие" Value="@Node.Operator"
               ValueChanged="@OnOperatorChanged" Variant="Variant.Outlined"
               Margin="Margin.Dense" Dense="true" Disabled="@(_descriptor is null)"
               Style="flex:1 1 120px;min-width:110px">
        @foreach (var op in _availableOperators)
        {
            <MudSelectItem Value="@op">@ClayFilterOperatorLabels.Get(op)</MudSelectItem>
        }
    </MudSelect>

    @if (_descriptor is not null)
    {
        <div style="flex:1 1 130px;min-width:120px">
            <ClayFilterValueEditor Type="@_descriptor" Value="@Node.Value"
                                    ValueChanged="@OnValueChanged" Options="@_options"
                                    Operator="@Node.Operator" />
        </div>
    }

    <MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small"
                   OnClick="@OnRemove" title="Удалить условие"
                   Style="flex:0 0 auto" />
</div>
```
- `Dense="true"` на `MudSelect` (плюс уже есть `Margin.Dense`) уменьшает высоту полей.
- `flex-wrap:wrap` — если по ширине не влезает, «Значение» перенесётся, но при обычной
  ширине всё в одну строку → карточка вдвое ниже.

### 2. `ClayFilterGroup.razor` — тоньше отступы
- Контейнер группы: `gap:6px;padding:6px` (для не-корневых `border-left:2px`,
  `padding-left:8px`); для `IsRoot` — без рамки и без левого паддинга.
- Ряд заголовка (переключатель + ✕) и блок кнопок добавления — `gap:6px`, без крупных отступов.

### 3. `ClayFilterDialog.razor` — плотнее рабочая область
- Описание: `Class="pa-1 mb-1"` (вместо `pa-2 mb-2`).
- Рабочий контейнер: ширину дать чуть больше, чтобы условие помещалось в строку —
  `width:600px`; высоту можно уменьшить, напр. `height:min(460px, 70vh)`.

### Критерии (дополнительно)
- [ ] Условие умещается в одну строку (Поле/Условие/Значение/✕) при обычной ширине.
- [ ] Карточки условий заметно ниже; отступы плотнее; читаемость сохранена.
