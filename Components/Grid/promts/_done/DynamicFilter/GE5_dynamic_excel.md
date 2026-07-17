> Часть плана «Печать и Excel динамического грида». Перед началом прочитай **GE0_README_dynamic_export.md** и **_readme_grid_dynamic.md**. Требует выполненного **GE4**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GE5 — выгрузка в Excel в динамическом режиме

Прочитать перед началом (обязательно, до написания кода):

- **`Components/Grid/ClayGridPageBase.Export.Excel.cs`, метод `IClayGridDataLoader.ExcelExportAsync` —
  эталон. Прочитай построчно: switch по режиму, проверка «нет данных», генератор, base64,
  имя файла, JS-скачивание, снекбары, `try/catch`.**
- `Components/Grid/ClayGrid.ExportMenu.cs` — `ExcelCurrentPageInternal`, `ExcelAllInternal`,
  `ExcelSelectedInternal`. **Все три начинаются с `if (DataLoader is null) return;`.**
  Обрати внимание на `_isExporting` + `StateHasChanged()` в `try/finally`.
- `Components/Grid/ExcelExportRequest.cs` — `ExcelExportMode`, `SelectedIds`, `VisibleColumns`.
- `Services/ClayGridExcelGenerator.cs` — перегрузка с `IClayGridCellReader` (GE1), `SetCellValue`,
  логика Excel Outline (`groupStack`, `expandedGroups`).
- `Components/Grid/ClayGrid.Dynamic.Export.cs` (GE3/GE4) — три метода загрузки строк,
  `CreateDynamicCellReader`, `DynamicExpandedGroups`.
- Найди `SanitizeFileName` в `ClayGridPageBase.Export.Excel.cs` — он понадобится.

## Задача

Ровно то же, что GE4, но для Excel. Отличий два: генератор возвращает `byte[]`, а не строку,
и есть общий метод `ExcelExportAsync(ExcelExportRequest)` вместо трёх раздельных.

## Изменить/создать

**1.** `ClayGrid.Dynamic.Export.cs` — экспорт в Excel:

```csharp
    /// <summary>
    /// Выгрузка в Excel в динамическом режиме. Аналог
    /// ClayGridPageBase.ExcelExportAsync: тот же генератор, те же снекбары и имя файла,
    /// но строки грузятся через ClayGrid.Dynamic.Export, а ячейки читаются
    /// ClayDynamicCellReader.
    /// </summary>
    private async Task DynamicExcelExportAsync(ExcelExportRequest request)
    {
        try
        {
            var columns = request.VisibleColumns;
            if (columns.Count == 0) return;

            var rowsToExport = request.Mode switch
            {
                ExcelExportMode.CurrentPage => await BuildDynamicExportRowsForCurrentPage(),
                ExcelExportMode.Selected    => await BuildDynamicExportRowsForSelected(request.SelectedIds),
                ExcelExportMode.All         => await BuildDynamicExportRowsForAll(),
                _                           => await BuildDynamicExportRowsForCurrentPage(),
            };

            if (rowsToExport.Count == 0)
            {
                Snackbar.Add("Нет данных для выгрузки", Severity.Warning);
                return;
            }

            var bytes = ClayGridExcelGenerator.ExportToExcel(
                request.Title, columns, rowsToExport, CreateDynamicCellReader(),
                DynamicExpandedGroups, request.FilterDescription, request.GroupDescription);

            var base64   = Convert.ToBase64String(bytes);
            var fileName = $"{SanitizeExportFileName(request.Title)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            await JS.InvokeVoidAsync("clayGridExcel.downloadFile", fileName, base64);
            Snackbar.Add($"Файл «{fileName}» выгружен", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Ошибка выгрузки: {ex.Message}", Severity.Error);
        }
    }
```

**`SanitizeExportFileName`.** В статике `SanitizeFileName` — приватный метод
`ClayGridPageBase`, гриду он недоступен. **Не копируй его тело в грид.** Вынеси в общее
место — статический класс `Services/ClayGridExportFileName.cs`:

```csharp
namespace Clayzor.Lib.Web.Controls.Services;

/// <summary>Имя файла выгрузки: убирает символы, недопустимые в имени файла.</summary>
public static class ClayGridExportFileName
{
    /// <summary>Заменяет недопустимые символы. Пустой заголовок → «Данные».</summary>
    public static string Sanitize(string title)
    {
        /* … перенеси СЮДА тело ClayGridPageBase.SanitizeFileName без изменений … */
    }
}
```

и замени `ClayGridPageBase.SanitizeFileName` на вызов `ClayGridExportFileName.Sanitize(...)`,
удалив приватный метод. Имя файла в обоих режимах обязано формироваться одинаково — иначе
пользователи получат разные правила для одного и того же продукта. В гриде тогда вызывай
`ClayGridExportFileName.Sanitize(request.Title)` напрямую, без обёртки.

**2.** `ClayGrid.ExportMenu.cs` — диспетчер в трёх методах. Шаблон:

```csharp
    private async Task ExcelCurrentPageInternal()
    {
        if (!Dynamic && DataLoader is null) return;
        var columns = await ResolveExportColumnsAsync("выгрузки в Excel (текущая страница)");
        if (columns is null) return;

        _isExporting = true;
        StateHasChanged();
        try
        {
            var request = new ExcelExportRequest
            {
                Mode              = ExcelExportMode.CurrentPage,
                Title             = Title,
                VisibleColumns    = columns,
                FilterDescription = BuildFilterDescription(),
                GroupDescription  = BuildGroupDescription(),
            };

            if (Dynamic)
                await DynamicExcelExportAsync(request);
            else
                await DataLoader!.ExcelExportAsync(request);
        }
        finally
        {
            _isExporting = false;
            StateHasChanged();
        }
    }
```

Аналогично `ExcelAllInternal` (`Mode = All`) и `ExcelSelectedInternal`
(`Mode = Selected`, `SelectedIds = _selectedIds.ToList()`, плюс отдельная строка
`if (_selectedIds.Count == 0) return;`).

`ExcelExportRequest` собирается ОДИН раз и уходит в обе ветки — не дублируй его сборку внутри
`if/else`.

`_isExporting` + `StateHasChanged()` в `try/finally` — **не трогать**: это спиннер на кнопке,
он режим-агностичен.

## Не делай

Не меняй `ClayGridExcelGenerator` (GE1 его уже подготовил): стилизацию ClosedXML, Excel Outline,
авто-ширину, `SetCellValue`. Не трогай `clayGridExcel.js`. Не меняй `ExcelExportRequest` —
он публичный контракт. Не реализовывай `IClayGridDataLoader` в гриде. Не копируй
`SanitizeFileName` — выноси в общее место. Не показывай меню — это GE6.

## Проверка

Меню Excel в динамическом режиме ещё скрыто (GF15) — **временно** верни его: в `ClayGrid.razor`
условие подменю «Выгрузка в Excel» → `@if (ShowExcel)`, в `Home.razor` → `ShowExcel="true"`.
После проверки верни как было.

`?id=140&CLID=9`, «Excel → Текущая страница», «Как на странице» — открой скачанный файл:

- заголовок (`Запросы.Заголовок`) в первой строке, шапка колонок, полосатые строки —
  как в статическом экспорте;
- **все ячейки заполнены**;
- **колонка Тип 1 (Число) — ЧИСЛО**: выравнено вправо, в строке формул число, а не текст,
  по колонке считается сумма. **Это главная проверка шага** (см. GE2: типы 1/3/7 отдаются
  сырыми именно ради этого);
- **колонка Тип 3 (Дата) — ДАТА**: формат `dd.MM.yyyy`, сортируется как дата;
- **колонка Тип 7 (Булево)** — «Да»/«Нет» по центру;
- **колонка Тип 5** — наименование, не код;
- **колонка Тип 10** — время клиента; в ячейке ТЕКСТ, не дата (известный компромисс из GE2 —
  формат берётся из справочника);
- **колонка Тип 12** — полный текст;
- имя файла: `Заголовок_20260715_143012.xlsx`, недопустимые символы заменены;
- снекбар «Файл «…» выгружен»;
- «Excel → Все данные» → в файле ВСЕ записи; на кнопке во время выгрузки виден спиннер
  (`_isExporting`);
- фильтр активен → в файле строка с описанием фильтра под заголовком, данные отфильтрованы;
- грид без записей (фильтр в ноль) → снекбар «Нет данных для выгрузки», файл не скачался;
- сломать `Запросы.SQL` → снекбар «Ошибка выгрузки: …», спиннер снят, грид жив.

С `GF13`: отметить 3 записи → «Excel → Выбранные (3)» → в файле ровно 3 строки.

**С группировкой (только если сделан GG7):** сгруппировать, часть групп раскрыть,
«Excel → Все данные» → в файле строки заголовков групп с наименованиями и счётчиками; слева
работает Excel Outline (+/−); **свёрнутые в гриде группы свёрнуты и в файле**, раскрытые —
раскрыты (`DynamicExpandedGroups` → `expandedGroups` генератора). Двухуровневая группировка →
вложенный Outline. «Excel → Выбранные» с группировкой → заголовки только тех групп, где есть
выбранные, а счётчик в заголовке — ПОЛНЫЙ размер группы (GE3). Если GG7 не сделан — напиши
в отчёте, что не проверялось.

Статический режим (`MedicalTests.razor`): все три пункта Excel работают как раньше, имя файла
не изменилось (`SanitizeFileName` переехал, но поведение то же).
