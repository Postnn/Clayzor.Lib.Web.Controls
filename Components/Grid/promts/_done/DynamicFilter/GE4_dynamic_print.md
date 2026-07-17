> Часть плана «Печать и Excel динамического грида». Перед началом прочитай **GE0_README_dynamic_export.md** и **_readme_grid_dynamic.md**. Требует выполненных **GE1**, **GE2**, **GE3**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GE4 — печать в динамическом режиме

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGrid.ExportMenu.cs` — `PrintCurrentPageInternal`, `PrintAllInternal`,
  `PrintSelectedInternal`. **Все три начинаются с `if (DataLoader is null) return;` — вот это
  и чиним.** Обрати внимание на порядок: диалог колонок → спиннер → построение HTML →
  скрыть спиннер → печать → catch → снекбар.
- `Components/Grid/ClayGridPageBase.cs` — `IClayGridDataLoader.BuildPrintHtmlAsync`,
  `BuildPrintHtmlForCurrentPageAsync`; `ClayGridPageBase.Export.Selected.cs` —
  `BuildPrintHtmlForSelectedAsync`. Эталон вызова генератора.
- `Services/ClayGridPrintHtmlGenerator.cs` — перегрузка с `IClayGridCellReader` (GE1).
- `Components/Grid/Dynamic/ClayDynamicCellReader.cs` (GE2) — его конструктор.
- `Components/Grid/ClayGrid.Dynamic.Export.cs` (GE3) — три метода загрузки строк.
- `Components/Grid/ClayGrid.Dynamic.cs` — `_dynamicCols`, `_dynamicLookups`,
  `_dynamicIconLookups`, `_clientOffset`, `_dynamicExpandedGroups` (GG3, если сделан).

## Задача

Меню печати вызывает три метода, и каждый уходит в `DataLoader`, которого в динамическом режиме
нет. Нужны динамические аналоги и диспетчер между ними.

## Изменить/создать

**1.** `ClayGrid.Dynamic.Export.cs` — фабрика читателя ячеек и три метода печати:

```csharp
    /// <summary>
    /// Читатель ячеек динамического грида. Создаётся на каждый экспорт: _clientOffset
    /// может измениться (GF11), справочники — нет, но они передаются по ссылке.
    /// </summary>
    private ClayDynamicCellReader CreateDynamicCellReader()
        => new(_dynamicCols, _dynamicLookups, _dynamicIconLookups, _clientOffset);

    /// <summary>Раскрытые группы для Excel Outline и печати. Пусто, если группировки нет.</summary>
    private HashSet<string> DynamicExpandedGroups => _dynamicExpandedGroups;

    private async Task<string> BuildDynamicPrintHtmlForCurrentPage(
        IReadOnlyList<ClayColumnMeta> columns, string? filterDescription, string? groupDescription)
    {
        var rows = await BuildDynamicExportRowsForCurrentPage();
        return ClayGridPrintHtmlGenerator.Build(
            Title, columns, rows, CreateDynamicCellReader(), DynamicExpandedGroups,
            filterDescription, groupDescription);
    }

    private async Task<string> BuildDynamicPrintHtmlForAll(
        IReadOnlyList<ClayColumnMeta> columns, string? filterDescription, string? groupDescription)
    {
        var rows = await BuildDynamicExportRowsForAll();
        return ClayGridPrintHtmlGenerator.Build(
            Title, columns, rows, CreateDynamicCellReader(), DynamicExpandedGroups,
            filterDescription, groupDescription);
    }

    private async Task<string> BuildDynamicPrintHtmlForSelected(
        IReadOnlyList<ClayColumnMeta> columns, IReadOnlyCollection<int> selectedIds,
        string? filterDescription, string? groupDescription)
    {
        var rows = await BuildDynamicExportRowsForSelected(selectedIds);
        return ClayGridPrintHtmlGenerator.Build(
            Title, columns, rows, CreateDynamicCellReader(), DynamicExpandedGroups,
            filterDescription, groupDescription);
    }
```

Если `_dynamicExpandedGroups` в коде нет — значит GG3 не выполнен. Тогда `DynamicExpandedGroups`
верни как `[]` (новый пустой `HashSet`) и **напиши в отчёте**, что печать раскрытых групп
заработает вместе с GG3. Не создавай второе поле состояния групп.

**2.** `ClayGrid.ExportMenu.cs` — заменить `if (DataLoader is null) return;` на диспетчер.
Ровно три метода, шаблон одинаковый:

```csharp
    private async Task PrintCurrentPageInternal()
    {
        if (!Dynamic && DataLoader is null) return;
        var columns = await ResolveExportColumnsAsync("печати (текущая страница)");
        if (columns is null) return;

        var spinnerId = Id + "-print-spinner";
        _ = JS.InvokeVoidAsync("clayGridPrint.showSpinner", spinnerId);
        try
        {
            var html = Dynamic
                ? await BuildDynamicPrintHtmlForCurrentPage(
                      columns, BuildFilterDescription(), BuildGroupDescription())
                : await DataLoader!.BuildPrintHtmlForCurrentPageAsync(
                      columns, Title, BuildFilterDescription(), BuildGroupDescription());
            await JS.InvokeVoidAsync("clayGridPrint.hideSpinner", spinnerId);
            await JS.InvokeAsync<object>("clayGridPrint.printHtml", html);
        }
        catch (Exception ex)
        {
            await JS.InvokeVoidAsync("clayGridPrint.hideSpinner", spinnerId);
            Snackbar.Add($"Ошибка печати: {ex.Message}", Severity.Error);
        }
    }
```

Аналогично `PrintAllInternal` (`BuildDynamicPrintHtmlForAll` / `DataLoader!.BuildPrintHtmlAsync`)
и `PrintSelectedInternal`:

```csharp
    private async Task PrintSelectedInternal()
    {
        if (!Dynamic && DataLoader is null) return;
        if (_selectedIds.Count == 0) return;
        /* … далее по шаблону, динамическая ветка:
           await BuildDynamicPrintHtmlForSelected(columns, _selectedIds.ToList(), …) … */
    }
```

Обрати внимание на три вещи:

- **`if (!Dynamic && DataLoader is null) return;`** — а не `if (DataLoader is null && !Dynamic)`.
  Смысл тот же, но читается как «если не динамика и загрузчика нет — выходим». Проверка
  `_selectedIds.Count == 0` вынесена отдельной строкой — она нужна обоим режимам.
- **`Title` не передаётся в динамическую ветку** — методы GE3/GE4 берут его сами из свойства
  грида. В статике `Title` передаётся аргументом, потому что вызов уходит на страницу.
  Не «унифицируй».
- **Спиннер, `try/catch`, снекбар, `ResolveExportColumnsAsync`, тексты контекста
  («печати (текущая страница)») — НЕ трогать.** Они режим-агностичны.

## Не делай

Не реализовывай `IClayGridDataLoader` в гриде и не создавай `ClayGridDynamicDataLoader` —
интерфейс существует для связи «грид → страница», а в динамическом режиме страницы нет, лишний
слой не нужен. Не меняй `ClayGridPageBase` и его реализацию печати. Не меняй генератор
(GE1 его уже подготовил). Не трогай `clayGridPrint.js`. Не показывай меню — это GE6. Excel —
это GE5, здесь не трогай.

## Проверка

Меню печати в динамическом режиме ещё скрыто (GF15), поэтому **временно** верни его: в
`ClayGrid.razor` замени условие подменю «Печать» на `@if (ShowPrint)` и добавь в `Home.razor`
`ShowPrint="true"`. После проверки — верни как было (окончательно включит GE6).

`?id=140&CLID=9`:

- «Печать → Текущая страница» → диалог выбора колонок; «Как на странице» → открылась печатная
  форма, **все ячейки заполнены**, колонки и порядок как в гриде;
- **колонка Тип 5 (Тип исследования)** — наименование, а не код;
- **колонка Тип 10** — время клиента, а не UTC (сверь с ячейкой на экране);
- **колонка Тип 12** — ПОЛНЫЙ текст без «…» (сознательное отличие от экрана, см. GE2);
- **колонка Тип 8 (HTML)** — текст без тегов;
- **колонка Тип 3 (Дата)** — `dd.MM.yyyy`; **Тип 7 (Булево)** — иконка ✔/✘;
- в форме нет тулбара, пагинатора, чекбоксов (`@media print` CSS из генератора);
- заголовок формы = `Запросы.Заголовок`, а не «Список»;
- «Печать → Все данные» → в форме ВСЕ записи, а не страница; в профайлере один `SELECT`
  без `ROW_NUMBER()`;
- навесить фильтр → в форме только отфильтрованные, под заголовком видно описание фильтра
  (`BuildFilterDescription`);
- «Настроить» в диалоге колонок → убрать колонку, поменять порядок → форма построена по
  выбору пользователя, грид не изменился;
- «Отмена» в диалоге → ничего не произошло, спиннер не завис;
- сломать `Запросы.SQL` (например, опечатка в имени таблицы) → снекбар «Ошибка печати: …»,
  спиннер скрыт, грид жив.

С `GF13` (выбор строк): `SelectVisible="true"`, отметить 3 записи → «Печать → Выбранные (3)» →
в форме ровно эти 3.

**С группировкой (только если сделан GG7):** сгруппировать, раскрыть группу,
«Печать → Текущая страница» → строки групп с наименованиями и «(N шт.)», отступы по глубине,
детали раскрытых групп ЦЕЛИКОМ (не обрезаны пагинацией). «Печать → Все данные» → всё дерево.
Если GG7 не сделан — напиши в отчёте, что группированная печать не проверялась.

Статический режим (`MedicalTests.razor`): все три пункта печати работают как раньше.
