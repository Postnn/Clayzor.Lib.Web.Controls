# F4. Диалог: реальное ограничение высоты + переключатель И/ИЛИ

Файлы: `Components/Grid/Filter/ClayFilterDialog.razor`,
`Components/Grid/Filter/ClayFilterGroup.razor`,
`Components/Grid/ClayGrid.Filtering.cs`.
Уточняет F3 (layout применён, но диалог не привязан к высоте окна).

## Проблема 1 — форма уезжает за пределы экрана
`max-height:70vh` стоит на внутреннем контейнере, но сам `MudDialog` не ограничен
высотой вьюпорта: открывается с `DragMode = MudDialogDragMode.Simple` (плавающий
режим MudExtensions снимает штатный cap высоты). Высота = заголовок + до 70vh +
футер > 100vh → кнопки под экраном.

### Исправить: привязать высоту всего диалога к окну (scoped-стиль) + flex до дерева
1. `ClayGrid.Filtering.cs`, `OpenCompositeFilterDialog` — убрать перетаскиваемый режим
   (он и ломает cap высоты), диалог оставить обычным центрированным:
   ```csharp
   var options = new DialogOptionsEx
   {
       MaxWidth  = MaxWidth.Small,
       FullWidth = false,
       // DragMode убрать (или не задавать). Если перетаскивание всё же нужно —
       // тогда обязателен явный max-height диалога из scoped-стиля ниже.
   };
   ```
2. `ClayFilterDialog.razor` — задать диалогу класс и **scoped** (не глобальный!) стиль,
   который каппит высоту и делает контент flex-колонкой без собственного скролла:
   ```razor
   <MudDialog Class="clay-filter-dialog">
       ...
   </MudDialog>

   <style>
       /* Ограничено префиксом .clay-filter-dialog — на другие диалоги не влияет */
       .clay-filter-dialog .mud-dialog { max-height: 90vh; }
       .clay-filter-dialog .mud-dialog-content {
           display: flex; flex-direction: column;
           overflow: hidden;      /* скроллит внутренняя зона дерева, а не сам content */
           min-height: 0;
       }
   </style>
   ```
3. Внутренний контейнер `DialogContent` — **убрать** фиксированный `max-height:70vh`,
   растянуть на доступную высоту через flex:
   ```razor
   <DialogContent>
       <div style="display:flex;flex-direction:column;flex:1 1 auto;min-height:0;
                   width:560px;max-width:100%">

           @if (!string.IsNullOrEmpty(_draftDescription))
           {
               <MudPaper Elevation="0" Class="pa-2 mb-2"
                         Style="flex:0 0 auto;background:var(--mud-palette-background-grey)">
                   <MudText Typo="Typo.body2">@_draftDescription</MudText>
               </MudPaper>
           }

           <div style="flex:1 1 auto;overflow-y:auto;min-height:0">
               <ClayFilterGroup Node="@_draft" Columns="@Columns"
                                 LookupOptions="@LookupOptions" IsRoot="true"
                                 OnChanged="@OnDraftChanged" />
           </div>
       </div>
   </DialogContent>
   ```
Итог: `.mud-dialog` ≤ 90vh; `.mud-dialog-content` — flex-колонка без своего скролла;
описание фиксировано (`flex:0 0 auto`), дерево скроллится (`flex:1;overflow-y:auto;
min-height:0`); `DialogActions` всегда видны.

Критерий: сколько бы условий ни добавили — кнопки «Сбросить/Отмена/Применить»
и текст фильтра остаются на месте, прокручивается только список условий.

## Проблема 2 — переключатель И/ИЛИ: выбранная «И» не читается
Причина: в `ClayFilterGroup.razor` тумблер — `MudButtonGroup Variant="Outlined"`,
внутри которого выбранная кнопка переключается на `Variant.Filled`. Смешение вариантов
в группе ломает цвет текста: у выбранной (тёмно-синий фон) текст остаётся тёмным.

### Исправить: использовать штатный `MudToggleGroup`
```razor
<MudToggleGroup T="LogicalOperator"
                Value="@Node.Logic"
                ValueChanged="@SetLogic"
                Color="Color.Primary"
                Size="Size.Small"
                CheckMark="false"
                SelectionMode="SelectionMode.SingleSelection">
    <MudToggleItem Value="@LogicalOperator.And"  Text="И" />
    <MudToggleItem Value="@LogicalOperator.Or"   Text="ИЛИ" />
</MudToggleGroup>
```
`MudToggleGroup` сам обеспечивает контраст выбранного пункта (белый текст на
залитом фоне). Сигнатуру `SetLogic(LogicalOperator)` менять не нужно.

Альтернатива (если `MudToggleGroup` недоступен в текущей версии MudBlazor): оставить
две отдельные `MudButton` **без** обёртки `MudButtonGroup`, выбранная —
`Variant="Variant.Filled" Color="Color.Primary"` (гарантирует белый текст),
невыбранная — `Variant="Variant.Outlined" Color="Color.Default"`.

Также: убрать `Disabled="@(Node.Nodes.Count < 2)"` с переключателя (или оставить, но
проверить контраст в disabled) — логика группы может задаваться и при одном узле,
а «серый» вид сбивает.

Критерий: выбранный оператор (И или ИЛИ) читается всегда; переключение работает.

## Общий критерий
- [ ] `dotnet build` без ошибок; поведение фильтра не изменилось.
- [ ] Никаких глобальных (нескоупленных) стилей — только под `.clay-filter-dialog`.
