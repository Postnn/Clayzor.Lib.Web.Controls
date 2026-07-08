# V10. Чекбоксы диалога значения — как в выборе записей грида (фикс багов 3–6)

После V9 остались баги 3, 4, 5, 6. Причина одна: проект на **MudBlazor 9**, где у
`MudCheckBox` параметры называются `Value`/`ValueChanged`. Использованные в диалоге
`Checked`/`CheckedChanged` в v9 **не существуют** — MudBlazor молча складывает их в
`UserAttributes` (нет ни ошибки сборки, ни рантайма), поэтому привязка не работает:
галочки не читают `_checkedValues`, обработчики не вызываются.

Решение (как просил заказчик): не чинить API `MudCheckBox`, а сделать чекбоксы
**так же, как выбор записей в гриде** — кастомные `<span>` в кликабельном `<div>`
с `@onclick`. Это и выглядит одинаково, и работает независимо от версии MudBlazor.

Файл: `Components/Grid/ClayColumnValueFilterDialog.razor`.

## Эталон — из `ClayGrid.razor` (сервисная колонка выбора)
Три состояния рисуются вложенными `<span>` (16×16, рамка + внутренний элемент):
- **indeterminate**: рамка `#757575`, внутри квадрат 8×8 `#424242`;
- **checked**: рамка `#424242`, внутри «галочка» (span 4×7, `border-width:0 2px 2px 0`,
  `transform:rotate(45deg)`);
- **unchecked**: пустая рамка `#757575`.
Обёртка — `<div @onclick=... style="display:flex;align-items:center;...cursor:pointer;user-select:none">`.

## Замена разметки
Заменить весь блок `else { … }` со списком значений (три `MudCheckBox`:
«Выделить все», значения, «(пустые)») на разметку ниже. Вверху блока объявляется
локальный шаблон `glyph`, чтобы не дублировать стили.

```razor
else
{
    @{
        // Единый глиф чекбокса (как в шапке грида). state: null=частично, true=вкл, false=выкл
        RenderFragment<bool?> glyph = state =>
    @<span style="display:inline-flex;align-items:center;justify-content:center;width:16px;height:16px;border:2px solid @(state == true ? "#424242" : "#757575");border-radius:2px;background:#fff;box-sizing:border-box;flex-shrink:0">
        @if (state is null)
        {
            <span style="display:block;width:8px;height:8px;background:#424242"></span>
        }
        else if (state == true)
        {
            <span style="display:block;width:4px;height:7px;border:solid #424242;border-width:0 2px 2px 0;transform:rotate(45deg);margin-top:-1px"></span>
        }
    </span>;
    }

    @* ── «Выделить все» — sticky, tri-state (треб. 6) ── *@
    <div class="value-filter-select-all">
        <div @onclick="ToggleSelectAll"
             style="display:flex;align-items:center;gap:8px;cursor:pointer;user-select:none;padding:4px 0;font-weight:bold">
            @glyph(IsSelectAllState)
            <span>(Выделить все)</span>
        </div>
    </div>

    @* ── Прокручиваемый список значений ── *@
    <div class="value-filter-values-scroll">
        @foreach (var value in _result.Values)
        {
            var captured  = value;
            var isChecked = _checkedValues.Contains(captured);
            <div @onclick="() => ToggleValue(captured)"
                 style="display:flex;align-items:center;gap:8px;cursor:pointer;user-select:none;padding:4px 0">
                @glyph(isChecked)
                <span>@FormatValue(value)</span>
            </div>
        }

        @if (_result.HasBlanks)
        {
            <div @onclick="ToggleBlank"
                 style="display:flex;align-items:center;gap:8px;cursor:pointer;user-select:none;padding:4px 0">
                @glyph(_blankChecked)
                <span>(пустые)</span>
            </div>
        }
    </div>
}
```
Примечание: `@glyph(isChecked)` / `@glyph(_blankChecked)` передают `bool` — он
неявно приводится к `bool?` (true/false, значит рисуется вкл/выкл, без
«частично»); `@glyph(IsSelectAllState)` передаёт `bool?` (в т.ч. null → «частично»).
Если синтаксис `@<...>` в `@{ }` вызовет затруднения — заменить `glyph(...)` на
инлайн-повтор тех же трёх `<span>`-вариантов в каждом месте (как в `ClayGrid.razor`).

## Обработчики (`@code`)
Удалить старые `OnSelectAllToggle(bool)`, `OnValueCheckedChanged(object?,bool)` и
инлайновый `CheckedChanged="v => _blankChecked = v"`. Добавить toggle-методы:
```csharp
private void ToggleSelectAll()
{
    if (_result is null) return;
    if (IsSelectAllState == true)            // сейчас всё → снять всё
    {
        _checkedValues.Clear();
        _blankChecked = false;
    }
    else                                     // иначе → выбрать всё
    {
        _checkedValues = _result.Values.ToHashSet();
        _blankChecked = _result.HasBlanks;
    }
    StateHasChanged();
}

private void ToggleValue(object? value)
{
    if (!_checkedValues.Add(value))          // Add вернул false → уже было → снять
        _checkedValues.Remove(value);
    StateHasChanged();
}

private void ToggleBlank()
{
    _blankChecked = !_blankChecked;
    StateHasChanged();
}
```
`IsSelectAllState` (bool?-getter) оставить без изменений — он теперь только для
отображения глифа и решения в `ToggleSelectAll`. `Apply()`,
`OnInitializedAsync` (дефолт «выбраны все»), `FormatValue` — не трогать, они уже
корректны.

## Почему это чинит 3–6
- **4** (дефолт «все выбраны») и **5/6** (select-all): `_checkedValues`
  заполняется в `OnInitializedAsync`, а глиф читает его напрямую и рисуется
  сразу; клики идут через `@onclick` → реально меняют состояние + `StateHasChanged`.
- **3** (фильтр не применяется): раньше `_checkedValues` не менялся (обработчики
  не звались), поэтому `Apply` строил пустой/снимающий фильтр. Теперь состояние
  верное → `Apply` строит `IN`/`NOT IN` (инверсия — треб. 14).

## Критерии
- [ ] В диалоге нет ни одного `MudCheckBox`; чекбоксы — кастомные `<span>` как в
      `ClayGrid.razor` (тот же вид, включая «частично» у «Выделить все»).
- [ ] При открытии (без активного фильтра) все значения и «(пустые)» отмечены.
- [ ] Клик по строке значения переключает её галочку; клик «Выделить все»
      ставит/снимает все; состояние сразу отражается.
- [ ] Снятие подмножества → «Применить» → грид фильтруется.
- [ ] `dotnet build` без ошибок.
