# F6. Диалог/панель: один скролл, текст вместо чипов, фокус на значение

Файлы: `Components/Grid/Filter/ClayFilterDialog.razor`,
`Components/Grid/ClayGrid.razor` (трей фильтра),
`Components/Grid/Filter/ClayFilterExpression.razor(.cs)`.
Закрывает пункты 1, 2.1, 3. Пункт 2.2 — отдельно (после уточнения).

## 1. Двойной скролл в диалоге
Причина: фиксированная высота стоит на внутреннем `div`, а у `.mud-dialog-content`
остаётся собственная прокрутка → две полосы. Сделать единственным скроллом зону условий.

`ClayFilterDialog.razor` — перенести фиксированную высоту на сам content (scoped),
внутренний контейнер растянуть на 100%:
```razor
<style>
    .clay-filter-dialog .mud-dialog-content {
        height: min(460px, 70vh);   /* фиксированная высота — тут, а не на вложенном div */
        display: flex; flex-direction: column;
        overflow: hidden;           /* сам content не скроллит */
        min-height: 0;
    }
</style>
```
```razor
<DialogContent>
    <div style="display:flex;flex-direction:column;height:100%;width:600px;max-width:100%">
        @if (!string.IsNullOrEmpty(_draftDescription))
        {
            <MudPaper Elevation="0" Class="pa-1 mb-1"
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
Убрать любой `max-height`/`height` с внутренних `div` (высоту держит только content).
Критерий: в диалоге ровно одна полоса прокрутки — у списка условий.

## 2.1. В составном режиме — текст фильтра вместо чипов
Сейчас при наличии составных условий рендерятся и текст, и чипы/сегменты. Оставить
только текст.

`ClayGrid.razor`, блок трея (`@if (_filterTrayExpanded)`):
```razor
@if (_hasComposite)
{
    @* Только текст всего фильтра; клик по нему открывает диалог настраиваемого фильтра *@
    <div class="filter-chip filter-chip--composite" style="cursor:pointer"
         @onclick="@OpenCompositeFilterDialog">
        <span class="chip-label">@BuildFilterDescription()</span>
        <MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small"
                       OnClick="@ClearAllFilters" Class="chip-remove-btn pa-0"
                       Style="width:18px;height:18px" title="Очистить фильтр"
                       @onclick:stopPropagation="true" />
    </div>
}
else
{
    @* существующие чипы по ColumnDialogLeaves — без изменений *@
}
```
Убрать в составной ветке цикл по сегментам (`_filterSegments`) — вместо него один
текст `BuildFilterDescription()`. `BuildFilterSegments()` в этой ветке больше не нужен.
Критерий: есть составные условия → в трее один текст фильтра, без чипов/сегментов.

## 3. Фокус на «Значение» после выбора колонки
Сейчас `AutoFocus` у редактора значения срабатывает только при первом рендере.
Нужно переносить фокус на значение при **смене колонки**.

`ClayFilterExpression`:
- Ввести счётчик перемонтирования редактора значения и флаг автофокуса:
  ```csharp
  private int _valueKey;          // меняется при смене колонки → ремоунт редактора
  private bool _focusValue;       // автофокус только после явной смены колонки
  ```
- В `OnColumnChanged` после установки колонки/сброса значения:
  ```csharp
  _valueKey++;
  _focusValue = true;
  ```
- В разметке навесить `@key` и `AutoFocus` на редактор значения:
  ```razor
  <ClayFilterValueEditor @key="_valueKey"
                          Type="@_descriptor" Value="@Node.Value"
                          ValueChanged="@OnValueChanged" Options="@_options"
                          Operator="@Node.Operator"
                          AutoFocus="@_focusValue" />
  ```
- После рендера сбросить флаг, чтобы фокус не «залипал»:
  ```csharp
  protected override void OnAfterRender(bool firstRender)
  {
      if (_focusValue) _focusValue = false;
  }
  ```
Смена `@key` перемонтирует редактор, и при `AutoFocus=true` фокус встаёт на поле
значения. Начальный рендер (`_valueKey==0`, `_focusValue==false`) фокус не крадёт.
Критерий: выбрал колонку → курсор в поле «Значение».

## Критерии
- [ ] Одна полоса прокрутки в диалоге.
- [ ] Составной режим: только текст фильтра, чипов нет.
- [ ] После выбора колонки фокус в «Значение».
- [ ] `dotnet build` без ошибок; без глобальных стилей.

---

## Дополнение к п.3 — фокус на «Значение» также после смены оператора
Помимо `OnColumnChanged`, тот же приём применить в `OnOperatorChanged`
(bump `_valueKey` + `_focusValue = true`). Так после выбора и поля, и условия
курсор встаёт в «Значение». Для операторов без значения (IsEmpty/IsNull/…) редактор
скрыт — автофокус просто не сработает, дополнительных проверок не нужно.
