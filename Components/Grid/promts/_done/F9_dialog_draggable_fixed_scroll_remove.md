# F9. Диалог фильтра: перетаскивание + фикс. высота + один скролл + удаление колоночных условий

Файлы: `Components/Grid/Filter/ClayFilterDialog.razor`,
`Components/Grid/ClayGrid.Filtering.cs`,
`Components/Grid/Filter/ClayFilterGroup.razor`.
Заменяет разметку диалога из F8 (F8-подход со sticky+нативным скроллом откатить).

## Почему прошлые попытки ломались
- Override `.clay-filter-dialog .mud-dialog-content` не срабатывал (класс уходил не на
  `.mud-dialog`) → `.mud-dialog-content` скроллился сам + наш внутренний скролл = двойной.
- Без фиксированной высоты и без `DragMode` диалог рос по контенту и не таскался.

Надёжное решение — стилизовать `.mud-dialog-content` **напрямую** через штатный параметр
`ContentStyle` компонента `MudDialog` (не зависит от того, куда уходит `Class`), и вернуть
перетаскивание. Фиксированная высота контента делает диалог ограниченным даже в
перетаскиваемом режиме.

## 1–3. Перетаскиваемый диалог с фикс. высотой и одним скроллом

### `ClayGrid.Filtering.cs` — вернуть DragMode составному диалогу
```csharp
var options = new DialogOptionsEx
{
    MaxWidth = MaxWidth.Small,
    FullWidth = false,
    CloseOnEscapeKey = true,
    DragMode = MudDialogDragMode.Simple,   // вернуть перетаскивание
};
```

### `ClayFilterDialog.razor` — ContentStyle + прямые flex-дети
```razor
<MudDialog ContentStyle="width:600px;max-width:100%;height:min(460px,80vh);
                         overflow:hidden;display:flex;flex-direction:column;gap:4px">
    <TitleContent>
        <MudText Typo="Typo.h6" Style="display:flex;align-items:center;gap:8px">
            <MudIcon Icon="@Icons.Material.Filled.FilterList" Size="Size.Small" />
            Настраиваемый фильтр
        </MudText>
    </TitleContent>

    <DialogContent>
        @* Прямые дети .mud-dialog-content (без обёртки!), чтобы работал flex-скролл *@

        @if (!string.IsNullOrEmpty(_draftDescription))
        {
            <MudPaper Elevation="0" Class="pa-1"
                      Style="flex:0 0 auto;background:var(--mud-palette-background-grey)">
                <MudText Typo="Typo.body2">@_draftDescription</MudText>
            </MudPaper>
        }

        <div style="flex:1 1 auto;overflow-y:auto;min-height:0">
            <ClayFilterGroup Node="@_draft" Columns="@Columns"
                              LookupOptions="@LookupOptions" IsRoot="true"
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
Ключевое:
- `ContentStyle` кладётся прямо на `.mud-dialog-content`: фиксированная высота
  `min(460px,80vh)`, `overflow:hidden` (сам content НЕ скроллит), `display:flex;column`.
- Описание и дерево — **прямые** дети `DialogContent` (никакой обёртки `<div width:600>`),
  иначе flex не разложит их и скролл не появится. Ширину задаёт `ContentStyle`.
- Описание `flex:0 0 auto` (фиксировано), дерево `flex:1 1 auto;overflow-y:auto;min-height:0`
  — **единственная** прокрутка.
- Убрать `position:sticky` и старую обёртку из F8.

> Если в вашей версии MudBlazor параметр называется иначе — искать у `MudDialog`
> свойство, стилизующее контент (`ContentStyle`/`ContentClass`). Класс `Class` на
> `<MudDialog>` НЕ использовать для этого — он ненадёжно доходит до `.mud-dialog-content`.

Критерии 1–3: диалог перетаскивается; высота постоянная и влезает в экран; в теле
диалога ровно одна полоса прокрутки (у списка условий); кнопки всегда видны.

## 4. Удаление колоночного условия прямо из формы
Сейчас лист `Source=ColumnDialog` в `ClayFilterGroup.razor` рисуется read-only блоком
«Редактируется в диалоге колонки: …» без кнопки удаления. Добавить крестик.

Найти блок (ветка `leaf.Source == ClayFilterSource.ColumnDialog`) и добавить удаление
через уже существующий `RemoveNode(capturedIndex)`:
```razor
<div style="display:flex;align-items:center;gap:8px;padding:6px 8px;
            border:1px solid var(--mud-palette-lines-default);border-radius:4px;
            color:var(--mud-palette-text-secondary)">
    <MudIcon Icon="@Icons.Material.Filled.FilterAlt" Size="Size.Small" />
    <MudText Typo="Typo.body2" Style="flex:1 1 auto">
        Редактируется в диалоге колонки: @GetLeafDescription(leaf)
    </MudText>
    <MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small"
                   OnClick="@(() => RemoveNode(capturedIndex))"
                   title="Удалить условие" Style="flex:0 0 auto" />
</div>
```
Удаление из черновика + «Применить» уберёт лист из дерева, а значит и чип колонки
в трее (чипы выводятся из тех же листьев). Редактирование значения по-прежнему —
через диалог колонки; здесь только удаление.

Критерий 4: колоночное условие можно удалить прямо из формы настраиваемого фильтра.

## Общие критерии
- [ ] Диалог перетаскивается; высота фиксирована и помещается на экран.
- [ ] Одна вертикальная полоса прокрутки (список условий).
- [ ] Колоночное условие удаляется из формы; после «Применить» исчезает и из грида.
- [ ] `dotnet build` без ошибок; без глобальных нескоупленных стилей.
