# F8. Убрать дублирование текста фильтра и двойной скролл диалога

Файлы: `Components/Grid/ClayGrid.razor`,
`Components/Grid/Filter/ClayFilterDialog.razor`.
Причины найдены по факту (не там, где чинили в F5/F6).

## 1. Двойное отображение текста фильтра (серый текст + чип)
Причина: помимо чипа в трее (ветка `@if (HasComposite)`), на экране рендерится блок
`clay-grid-print-descriptions` (в `ClayGrid.razor`, ~строки 152–167), который выводит
`filterDesc = BuildFilterDescription()`. По имени класса это **печатная** шапка, но она
не скрыта от экрана — отсюда серая копия над треем.

Исправить: показывать этот блок **только при печати**. Добавить CSS (в глобальный
стиль приложения или `<style>` в `ClayGrid.razor` — класс грид-специфичный):
```css
.clay-grid-print-descriptions { display: none; }
@media print {
    .clay-grid-print-descriptions { display: block; }
}
```
Разметку блока не трогать (он нужен для печати/экспорта). Чип в трее остаётся
единственным экранным представлением.

Критерий: на экране текст фильтра — один раз (в трее); при печати описание фильтра
присутствует в шапке.

## 2. Двойной скролл в диалоге
Причина: скролл-override завязан на `.clay-filter-dialog .mud-dialog-content`, но
MudExtensions-диалог вешает `Class` не на `.mud-dialog`, селектор не срабатывает —
`.mud-dialog-content` продолжает скроллиться дефолтом, плюс наш внутренний
`overflow-y:auto` даёт вторую полосу. У составного диалога `DragMode` уже убран,
поэтому MudBlazor сам каппит высоту — надёжнее отдать скролл штатному
`.mud-dialog-content`, а описание закрепить `position:sticky`.

Переписать `ClayFilterDialog.razor` проще (без своего `<style>`, без внутренней
скролл-зоны, без фиксированной высоты):
```razor
<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6" Style="display:flex;align-items:center;gap:8px">
            <MudIcon Icon="@Icons.Material.Filled.FilterList" Size="Size.Small" />
            Настраиваемый фильтр
        </MudText>
    </TitleContent>

    <DialogContent>
        <div style="width:600px;max-width:100%">

            @* Описание — прилипает к верху, не уезжает при прокрутке условий *@
            @if (!string.IsNullOrEmpty(_draftDescription))
            {
                <MudPaper Elevation="0" Class="pa-1 mb-1"
                          Style="position:sticky;top:0;z-index:2;
                                 background:var(--mud-palette-background-grey)">
                    <MudText Typo="Typo.body2">@_draftDescription</MudText>
                </MudPaper>
            }

            @* Условия — прокручиваются вместе со штатным .mud-dialog-content *@
            <ClayFilterGroup Node="@_draft"
                              Columns="@Columns"
                              LookupOptions="@LookupOptions"
                              IsRoot="true"
                              OnChanged="@OnDraftChanged" />
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
```
Убрать: `Class="clay-filter-dialog"`, весь блок `<style>…</style>`, внутренний
`<div style="…overflow-y:auto…">` и фиксированную высоту. Скролл теперь один —
у самого `.mud-dialog-content` (штатный), высота каппится MudBlazor (DragMode нет),
кнопки закреплены, описание — sticky.

Критерий: в диалоге одна полоса прокрутки; описание зафиксировано сверху; кнопки
всегда видны; при большом числе условий контент прокручивается.

## Общий критерий
- [ ] На экране текст фильтра отображается один раз.
- [ ] В диалоге ровно одна вертикальная полоса прокрутки.
- [ ] Печать сохраняет описание фильтра в шапке.
- [ ] `dotnet build` без ошибок.

## Примечание
Полоса прокрутки, видимая рядом с открытым выпадающим списком оператора (на скрине) —
это скролл самого выпадающего списка MudSelect, а не диалога; после переписывания
диалога у его тела остаётся одна полоса. Если у выпадающего списка visually двойной
скроллбар — это отдельный косметический нюанс MudSelect, не связанный с телом диалога;
при необходимости решается отдельно.
