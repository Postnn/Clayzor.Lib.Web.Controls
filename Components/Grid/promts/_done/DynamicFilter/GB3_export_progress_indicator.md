> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md** и **STYLE_RULES.md** (§2 запрещённые инлайны, §4 классы в app.css). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB3 — долгая операция экспорта/печати визуально не видна

Прочитать перед началом: `Components/Grid/ClayGrid.ExportMenu.cs` — целиком (шесть точек входа:
`PrintCurrentPageInternal`, `PrintAllInternal`, `PrintSelectedInternal`, `ExcelCurrentPageInternal`,
`ExcelAllInternal`, `ExcelSelectedInternal`, а также `ResolveExportColumnsAsync` и `_isExporting`);
`Components/Grid/ClayGrid.razor` — блок заголовка грида (`<MudText Typo="Typo.h5">@Title</MudText>`,
`@if (_isExporting)`, `<span id="@(Id + "-print-spinner")">`) и корневой `<MudPaper ... Style="position:relative" id="@Id">`;
`wwwroot/js/clayGridPrint.js` — `showSpinner` / `hideSpinner` / `printHtml`;
`app.css` (обе копии) — `.clay-print-spinner` и `@keyframes clay-print-spin`;
`STYLE_RULES.md` §4 — таблица паттернов.

## Дефект

Индикация долгой операции сейчас разная и в обоих случаях незаметная:

- **Excel** — `_isExporting = true; StateHasChanged();` → `MudProgressCircular Size.Small`
  сбоку от заголовка грида. 18 пикселей в левом верхнем углу, в противоположной от меню части
  экрана — человек, кликнувший «Все данные», туда не смотрит.
- **Печать** — свой JS-спиннер (`clayGridPrint.showSpinner`) в `<span>` рядом с тем же
  заголовком, мимо Blazor-рендера. `_isExporting` при печати не выставляется вообще.

Ни то, ни другое не блокирует UI: после закрытия меню грид выглядит рабочим — можно листать
страницы, жать «Экспорт» второй раз, менять сортировку — пока сервер собирает десятки тысяч
строк и генерирует книгу ClosedXML. С точки зрения человека «ничего не произошло».

Второе: `StateHasChanged()` только ставит рендер в очередь. Если между ним и первым настоящим
`await` окажется синхронная работа (а `ClayGridExcelGenerator.ExportToExcel` — синхронный и
CPU-тяжёлый), батч уедет клиенту позже, чем хотелось. Явный `await Task.Yield()` сразу после
`StateHasChanged()` снимает вопрос ценой одной строки.

Итог: нужен один заметный индикатор на обе операции, поверх грида, блокирующий повторный клик.

## Изменить/создать

**1.** `Components/Grid/ClayGrid.ExportMenu.cs` — единая обёртка занятости. Рядом с `_isExporting`:

```csharp
/// <summary>Подпись текущей долгой операции. null — операция не идёт.</summary>
private string? _busyLabel;

/// <summary>
/// Выполняет долгую операцию (печать/экспорт) с видимой блокирующей индикацией.
/// StateHasChanged ставит рендер в очередь, но батч уедет клиенту только когда метод
/// уступит поток — поэтому Task.Yield() перед работой обязателен: генерация книги
/// синхронна и до первого await может пройти секунды.
/// </summary>
/// <param name="label">Подпись под индикатором, напр. «Выгрузка в Excel…».</param>
/// <param name="work">Тело операции.</param>
private async Task RunBusyAsync(string label, Func<Task> work)
{
    _busyLabel   = label;
    _isExporting = true;
    StateHasChanged();
    await Task.Yield();
    try
    {
        await work();
    }
    finally
    {
        _isExporting = false;
        _busyLabel   = null;
        StateHasChanged();
    }
}
```

**2.** `Components/Grid/ClayGrid.ExportMenu.cs` — провести через `RunBusyAsync` все шесть точек
входа. Диалог выбора колонок (`ResolveExportColumnsAsync`) остаётся ДО обёртки — он не долгий и
ждёт человека. Печать: JS-спиннер (`showSpinner`/`hideSpinner`) убрать, вместо него — общий
индикатор; сам вызов `clayGridPrint.printHtml(html)` вынести ЗА пределы `RunBusyAsync` —
пока открыт системный диалог печати, оверлей висеть не должен. Пример для одной точки, остальные
пять — по образцу:

```csharp
private async Task PrintCurrentPageInternal()
{
    if (!Dynamic && DataLoader is null) return;
    var columns = await ResolveExportColumnsAsync("печати (текущая страница)");
    if (columns is null) return;

    string? html = null;
    await RunBusyAsync("Подготовка печатной формы…", async () =>
    {
        html = Dynamic
            ? await BuildDynamicPrintHtmlForCurrentPage(
                  columns, BuildFilterDescription(), BuildGroupDescription())
            : await DataLoader!.BuildPrintHtmlForCurrentPageAsync(
                  columns, Title, BuildFilterDescription(), BuildGroupDescription());
    });

    if (html is null) return;
    try
    {
        await JS.InvokeAsync<object>("clayGridPrint.printHtml", html);
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Ошибка печати: {ex.Message}", Severity.Error);
    }
}
```

Обработка ошибок построения формы: `try/catch` вокруг тела остаётся там же, где был
(снекбар «Ошибка печати: …»), — `RunBusyAsync` гасит индикатор в `finally` в любом случае.
Подписи: «Подготовка печатной формы…» для трёх печатных точек, «Выгрузка в Excel…» для трёх
экселевских.

**3.** `Components/Grid/ClayGrid.razor` — убрать из блока заголовка старую индикацию:

```razor
@if (_isExporting)
{
    <MudProgressCircular ... />
}
<span id="@(Id + "-print-spinner")" class="clay-print-spinner" style="display:none"></span>
```

и добавить оверлей внутрь корневого `<MudPaper ... Style="position:relative" id="@Id">`
(`position:relative` там уже есть — оверлею он и нужен), последним элементом перед закрытием
`MudPaper`:

```razor
<MudOverlay Visible="@_isExporting" Absolute="true" DarkBackground="true">
    <div class="clay-grid-busy">
        <MudProgressCircular Color="Color.Primary" Indeterminate="true" Size="Size.Large" />
        <span>@_busyLabel</span>
    </div>
</MudOverlay>
```

**4.** Кнопка групповых операций на время работы — недоступна (второй клик по «Все данные»
запускает второй тяжёлый запрос). В `ClayGrid.razor`, активатор `ClayMenu` с классом
`toolbar-batch-btn` — добавить `Disabled="@_isExporting"`. Если `ClayMenu` параметра `Disabled`
не имеет — **не расширяй компонент ради этого**, оверлей и так перехватывает клики; тогда просто
пропусти пункт 4 и напиши об этом в отчёте.

**5.** `app.css` — **в ОБЕИХ копиях** (`Clayzor.App.Web.MedicalTests/wwwroot/css/app.css` и
`Kesco.App.Web.Inventory/wwwroot/css/app.css`), одинаково:

```css
/* ── Индикатор долгой операции грида (печать/экспорт) ── */
.clay-grid-busy {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;
    padding: 20px 28px;
    background: var(--mud-palette-surface);
    border-left: 3px solid var(--clay-gold);
    color: var(--mud-palette-text-primary);
    font-size: var(--clay-font-size);
}
```

Удалить оттуда же `.clay-print-spinner` и `@keyframes clay-print-spin` — они осиротели нашей
правкой. Проверить grep-ом, что других ссылок на класс нет (в т.ч. в `@media print`).

**6.** `wwwroot/js/clayGridPrint.js` — удалить `showSpinner` и `hideSpinner` (и их экспорт из
IIFE). `printHtml` не трогать.

**7.** `STYLE_RULES.md` §4, таблица паттернов — строка «Индикатор долгой операции грида →
`MudOverlay` + класс `.clay-grid-busy`». Больше в документации ничего не менять.

## Не делай

- Не выноси генерацию книги/HTML в `Task.Run` — компонент Blazor не потокобезопасен, а батч
  с оверлеем уже уехал клиенту до начала синхронной работы. Если после фикса окажется, что
  оверлей всё равно появляется с задержкой — это отдельная задача, не расширяй эту.
- Не заводи прогресс в процентах и не дроби операцию на этапы — данных для процента нет
  (`QueryRowsAsync` атомарен). Индикатор индетерминантный.
- Не трогай `ResolveExportColumnsAsync` и `ClayColumnSettingsPromptDialog` — диалоги ждут
  человека, оверлей на время их показа не нужен.
- Не трогай снекбары «Файл «…» выгружен» / «Нет данных для выгрузки» / «Ошибка выгрузки» —
  завершение уже сообщается ими.
- Не трогай `ClayGridPageBase.Export.*` и `ClayGrid.Dynamic.Export.cs` — индикация живёт в
  точках входа меню, обе реализации через них и проходят.
- Не добавляй `_isExporting` в `IClayGrid` — это внутреннее состояние компонента.

## Проверка (ручная)

- `Kesco.App.Web.Inventory`, `?id=140`: «Групповые операции» → «Выгрузка в Excel» →
  «Все данные» → после диалога выбора колонок грид накрыт затемнением, по центру — крупный
  индикатор с подписью «Выгрузка в Excel…»; клики по гриду и пагинации не проходят; по
  завершении — оверлей исчез, снекбар «Файл «…» выгружен»;
- то же для «Текущая страница» и «Выбранные (N)» (нужен GB1);
- «Печать» → «Все данные» → оверлей с подписью «Подготовка печатной формы…»; в момент
  открытия системного диалога печати оверлея УЖЕ нет;
- отмена в диалоге выбора колонок → оверлей не появлялся, состояние грида прежнее;
- ошибка (временно испортить `Запросы.SQL`) → оверлей снят, снекбар с ошибкой, грид жив;
- в DOM после экспорта не осталось `<span id="…-print-spinner">`; в консоли нет обращений
  к `clayGridPrint.showSpinner`;
- тёмная тема → индикатор читаем, фон плашки из `--mud-palette-surface`;
- статический режим (`/medical-tests`): печать и Excel во всех трёх режимах — тот же оверлей,
  файл/печать как раньше;
- `dotnet build` зелёный (StyleGuard: в разметке только структурные инлайны, цвет — в `app.css`).
