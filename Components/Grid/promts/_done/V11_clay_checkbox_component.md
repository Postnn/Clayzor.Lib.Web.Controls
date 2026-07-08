# V11. Компонент `ClayCheckbox` (вынос кастомного чекбокса)

Кастомный `<span>`-чекбокс (как в выборе записей грида) сейчас дублируется в
нескольких местах: шапка выбора, строка группы, строка-деталь в `ClayGrid.razor`
и три места в `ClayColumnValueFilterDialog.razor` (после V10). Вынести в
переиспользуемый компонент `ClayCheckbox` и заменить все вхождения.

**Behavior-preserving рефакторинг**: вид и поведение не меняются, только
консолидация разметки. Зависит от V10 (в диалоге уже кастомные `<span>`).

## Соглашения (как `ClayButton`/`ClayMenu`)
Один файл `.razor` с `@code`, RU-докстринги на публичные члены, неймспейс
`Clayzor.Lib.Web.Controls.Components`. Компонент — «управляемый» (controlled):
состояние хранит родитель и передаёт через `State`, компонент по клику лишь
поднимает `OnToggle`, а родитель сам меняет состояние и перерисовывается — ровно
как сейчас работают все места использования.

## Новый файл `Components/ClayCheckbox.razor`
```razor
@namespace Clayzor.Lib.Web.Controls.Components

<div class="clay-checkbox @Class"
     style="display:inline-flex;align-items:center;gap:8px;user-select:none;@(Disabled ? "cursor:default;opacity:.5" : "cursor:pointer");@Style"
     title="@Title"
     @onclick="HandleClick"
     @onclick:stopPropagation="true">
    <span style="display:inline-flex;align-items:center;justify-content:center;width:16px;height:16px;border:2px solid @(State == true ? "#424242" : "#757575");border-radius:2px;background:#fff;box-sizing:border-box;flex-shrink:0">
        @if (State is null)
        {
            <span style="display:block;width:8px;height:8px;background:#424242"></span>
        }
        else if (State == true)
        {
            <span style="display:block;width:4px;height:7px;border:solid #424242;border-width:0 2px 2px 0;transform:rotate(45deg);margin-top:-1px"></span>
        }
    </span>
    @if (!string.IsNullOrEmpty(Label))
    {
        <span>@Label</span>
    }
</div>

@code {
    /// <summary>Состояние: <c>null</c> — «частично», <c>true</c> — отмечен, <c>false</c> — снят.</summary>
    [Parameter] public bool? State { get; set; } = false;

    /// <summary>Текстовая подпись справа от чекбокса (опционально).</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Тултип (HTML-атрибут title).</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Блокировка: клик не срабатывает, вид приглушённый.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Доп. CSS-класс обёртки.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Доп. inline-стиль обёртки.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Клик по чекбоксу (если не <see cref="Disabled"/>). Родитель сам меняет состояние.</summary>
    [Parameter] public EventCallback OnToggle { get; set; }

    private async Task HandleClick()
    {
        if (Disabled) return;
        await OnToggle.InvokeAsync();
    }
}
```
Примечания:
- Клик всегда `stopPropagation` — сохраняет текущее поведение строк грида (там уже
  `@onclick:stopPropagation="true"`), в диалоге безвреден.
- `State` — `bool?`, поэтому двухсостоянийные вызовы передают `bool` (неявно
  приводится) или `(bool?)expr`.
- Если `_Imports.razor` не подключает `...Components`, добавить `@using` или
  вызывать как `<ClayCheckbox>` там, где `ClayButton`/`ClayMenu` уже видны.

## Рефактор вызовов в `ClayGrid.razor`

### 1. Шапка выбора (сейчас `@onclick="OnHeaderTriToggle"` + три `<span>`)
Заменить внутренний `@if/else` со `<span>` на компонент, сохранив внешний
центрирующий `<div style="…justify-content:center;width:100%">`:
```razor
<div style="display:flex;align-items:center;justify-content:center;width:100%">
    <ClayCheckbox State="@(IsHeaderIndeterminate() ? null : (bool?)_selectAllChecked)"
                   Title="@(IsHeaderIndeterminate() ? "Выбраны не все" : _selectAllChecked ? "Снять выделение" : "Выделить всё")"
                   OnToggle="OnHeaderTriToggle" />
</div>
```

### 2. Строка группы (сейчас `@onclick="async () => await OnGroupTriToggle(gh)"` + `<span>`)
```razor
var gs = ComputeGroupCheckState(gh);
<ClayCheckbox State="@(gs.Indeterminate ? null : (bool?)gs.Checked)"
               Title="Переключить выделение группы"
               OnToggle="() => OnGroupTriToggle(gh)" />
```

### 3. Строка-деталь (сейчас `@onclick="async () => await OnRowSelectAsync(eid, !detailChecked)"` + `<span>`)
```razor
<ClayCheckbox State="(bool?)detailChecked"
               Title="Выбрать запись"
               OnToggle="() => OnRowSelectAsync(eid, !detailChecked)" />
```
Внешние центрирующие `<div>` ячеек оставить как есть; убрать только вложенные
`@onclick`-обёртки и `<span>`-глифы, которые теперь внутри компонента.

## Рефактор `ClayColumnValueFilterDialog.razor` (заменяет разметку из V10)
Удалить локальный шаблон `glyph` и три блока `<div @onclick=…>@glyph(...)…`.
Вместо них:
```razor
<div class="value-filter-select-all">
    <ClayCheckbox State="IsSelectAllState"
                   Label="(Выделить все)"
                   Style="font-weight:bold;padding:4px 0"
                   OnToggle="ToggleSelectAll" />
</div>

<div class="value-filter-values-scroll">
    @foreach (var value in _result.Values)
    {
        var captured = value;
        <ClayCheckbox State="(bool?)_checkedValues.Contains(captured)"
                       Label="@FormatValue(value)"
                       Style="padding:4px 0"
                       OnToggle="() => ToggleValue(captured)" />
    }
    @if (_result.HasBlanks)
    {
        <ClayCheckbox State="(bool?)_blankChecked"
                       Label="(пустые)"
                       Style="padding:4px 0"
                       OnToggle="ToggleBlank" />
    }
</div>
```
Обработчики `ToggleSelectAll` / `ToggleValue` / `ToggleBlank` и `IsSelectAllState`
из V10 не меняются.

## Критерии
- [ ] Создан `Components/ClayCheckbox.razor` (controlled: `State` + `OnToggle`,
      tri-state как в гриде, опц. `Label`/`Title`/`Disabled`/`Class`/`Style`).
- [ ] Все 3 места в `ClayGrid.razor` используют `ClayCheckbox`; вид и поведение
      выбора записей/групп не изменились.
- [ ] Диалог значения использует `ClayCheckbox` вместо inline-`<span>`; галочки и
      «Выделить все» работают как после V10.
- [ ] Нет дублирования `<span>`-глифа чекбокса в проекте.
- [ ] `dotnet build` без ошибок.
