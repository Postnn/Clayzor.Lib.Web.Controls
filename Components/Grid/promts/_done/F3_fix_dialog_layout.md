# F3. Компоновка и компактность диалога настраиваемого фильтра

Файлы: `Components/Grid/Filter/ClayFilterDialog.razor`,
`Components/Grid/Filter/ClayFilterExpression.razor`,
`Components/Grid/Filter/ClayFilterGroup.razor`,
`Components/Grid/ClayGrid.Filtering.cs` (опции диалога).
Строится поверх F1. Модель дерева не меняется.

## #1 — кнопки «Сбросить/Отмена/Применить» уходят за пределы видимости
Причина: глобальное правило `overflow:visible` (баг 5) отключает штатный скролл
контента `MudDialog`, контент растёт неограниченно, футер уезжает вниз.

Исправить:
- Убедиться, что глобального `.mud-dialog-content{overflow:...}` больше нет
  (должно быть сделано в F1 — проверить и удалить, если осталось).
- Ограничить по высоте **область условий** (дерево), а не весь диалог.
  `DialogActions` у `MudDialog` остаются закреплёнными, пока контент ограничен.
- Итоговая раскладка `DialogContent` — три зоны (см. #2).

## #2 — текстовое представление фильтра не должно прокручиваться
Раскладка `DialogContent` — flex-колонка с ограниченной высотой; прокручивается
**только** дерево, описание и футер фиксированы:

```razor
<DialogContent>
    <div style="display:flex;flex-direction:column;max-height:70vh;width:560px;max-width:100%">

        @* Фиксированное описание — вне зоны прокрутки *@
        @if (!string.IsNullOrEmpty(_draftDescription))
        {
            <MudPaper Elevation="0" Class="pa-2 mb-2"
                      Style="flex:0 0 auto;background:var(--mud-palette-background-grey)">
                <MudText Typo="Typo.body2">@_draftDescription</MudText>
            </MudPaper>
        }

        @* Прокручиваемая зона условий *@
        <div style="flex:1 1 auto;overflow-y:auto;min-height:0">
            <ClayFilterGroup Node="@_draft" Columns="@Columns"
                              LookupOptions="@LookupOptions" IsRoot="true"
                              OnChanged="@OnDraftChanged" />
        </div>
    </div>
</DialogContent>
```
Ключевое: `min-height:0` на скролл-контейнере (иначе flex не даст ему сжиматься и
скролл не появится); описание — `flex:0 0 auto`.

## #3 — редактор значения занимает всю ширину, справа пусто
Ограничить ширину полей строки условия, чтобы они не растягивались на всю форму.

В `ClayFilterExpression.razor`:
- Строку «Поле/Условие» не растягивать во всю ширину: у `MudSelect` вместо `flex:1`
  задать фиксированные ширины (например, «Поле» `min-width:180px;max-width:240px`,
  «Условие» `min-width:150px;max-width:200px`), кнопку удаления оставить справа.
- Редактор значения обернуть в контейнер с ограниченной шириной:
  ```razor
  <div style="max-width:280px">
      <ClayFilterValueEditor Type="@_descriptor" Value="@Node.Value"
                              ValueChanged="@OnValueChanged" Options="@_options"
                              Operator="@Node.Operator" />
  </div>
  ```
(При узком диалоге из #4 этого достаточно, чтобы убрать «пустоту справа».)

## #4 — диалог должен быть компактнее
1. Опции диалога (`ClayGrid.Filtering.cs`, метод `OpenCompositeFilterDialog`):
   ```csharp
   var options = new DialogOptionsEx
   {
       MaxWidth  = MaxWidth.Small,   // было Medium
       FullWidth = false,            // ширину задаёт контент (560px в #2)
       DragMode  = MudDialogDragMode.Simple,
   };
   ```
2. Убрать старый `min-width:480px;max-width:700px` (заменён контейнером `width:560px` из #2).
3. Уменьшить накопление отступа вложенности в `ClayFilterGroup.razor`: для не-корневых
   групп `padding` слева сделать меньше (например, `padding:6px 6px 6px 8px`,
   `border-left:2px`), чтобы глубокая вложенность не съедала ширину. Для `IsRoot`
   (после F1) — без рамки/отступа.
4. Кнопку удаления группы не отодвигать `MudSpacer`-ом в самый край при компактной
   ширине — допустимо оставить, но проверить, что строка заголовка группы не создаёт
   лишнюю ширину.

## Критерии
- [ ] При большом числе условий прокручивается только зона условий; описание и кнопки «Сбросить/Отмена/Применить» всегда видны.
- [ ] Текстовое представление фильтра зафиксировано сверху и не прокручивается.
- [ ] Поля условия не растянуты на всю форму; справа нет большого пустого поля.
- [ ] Диалог заметно компактнее по ширине; глубокая вложенность остаётся читаемой.
- [ ] Прочие диалоги приложения не затронуты (никаких глобальных стилей).
- [ ] `dotnet build` без ошибок.
