> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md**, **GB2_export_collapsed_groups.md** и **GB6_static_export_duplicates.md**. Требует выполненных **GB2**, **GB6**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB10 — печать: «Текущая страница» = строго экран, «Все данные» = весь список раскрытым

Прочитать перед началом: `Components/Grid/ClayGrid.ExportMenu.cs` — шесть точек входа;
`Services/ClayGridPrintHtmlGenerator.cs` — обе перегрузки `Build` (обрати внимание, что делает
параметр `expandedGroups` — **ничего**); `Components/Grid/ClayGrid.Dynamic.Export.cs` —
`BuildDynamicPrintHtmlForCurrentPage`, `BuildDynamicPrintHtmlForAll`,
`BuildDynamicPrintHtmlForSelected`, `BuildDynamicExportRowsForCurrentPage` (после GB2),
`BuildDynamicExportRowsForAll`, `DynamicExpandedGroups`;
`Components/Grid/ClayGridPageBase.cs` — `BuildPrintHtmlAsync` (~строка 209);
`Components/Grid/ClayGridPageBase.Export.Print.cs` — целиком (`BuildAllRowsForPrint`,
`BuildAllFlatRowsForPrint`, `BuildAllGroupedRowsForPrint`);
`Components/Grid/ClayGridPageBase.Export.Excel.cs` — `BuildPrintHtmlForCurrentPageAsync`,
`BuildAllRowsForExcel`, `BuildAllGroupedRowsForExcel`, `BuildExportRows` (после GB6);
`Components/Grid/ClayGridPageBase.Export.Selected.cs` — `BuildPrintHtmlForSelectedAsync`.

## Требование (решение заказчика, обсуждению не подлежит)

| Операция | Что должно быть в печатной форме |
|---|---|
| **Печать → Текущая страница** | **Строго то, что на экране.** Свёрнутая группа — одна строка-заголовок, без потомков. Раскрытая группа, разорванная пагинацией, — только видимая часть, догрузка хвоста не делается. |
| **Печать → Все данные** | **Весь список, все группы раскрыты.** Состояние свёрнутости на экране игнорируется. |
| **Печать → Выбранные (N)** | Как сейчас: выбранные записи + заголовки их групп. |
| **Excel (все три режима)** | **Как после GB2/GB6 — не трогать.** Данные в файле полные, свёрнутая на экране группа приезжает свёрнутым узлом Outline. |

Печать и Excel — разные требования, и с GB2 они поехали на одном билдере строк. GB10 их разводит.

## Дефект

**1. Динамическая печать «Текущая страница» печатает лишнее.**
`BuildDynamicPrintHtmlForCurrentPage` берёт строки у `BuildDynamicExportRowsForCurrentPage()` —
того самого метода, который GB2 (правильно, для Excel) научил догружать поддеревья всех
заголовков страницы, включая свёрнутые. Печать получила это заодно.

Статическая печать «Текущая страница» тем же дефектом **не страдает**:
`BuildPrintHtmlForCurrentPageAsync` отдаёт в генератор `_rows` — ровно экран. То есть режимы
уже разошлись, и правильный из них — статический.

**2. Печать «Все данные» расходится между режимами** — независимо от GB2:

- статика, `BuildAllGroupedRowsForPrint`: `ClayGroupingEngine.WalkTree(roots, _query.ExpandedGroups, 1, int.MaxValue, …)` — все страницы, но свёрнутые группы **не раскрываются**;
- динамика, `BuildDynamicExportRowsForAll` → `BuildDynamicGroupedExportRows`: раскрывается всё.

По решению заказчика правильный вариант — **динамический**: «Все данные» = весь список раскрытым.

**3. Корень, из-за которого это не ловилось.** `ClayGridPrintHtmlGenerator.Build` принимает
`HashSet<string>? expandedGroups` — и **не использует его ни разу**: `TBODY` печатает всё, что
пришло в `rows`. Параметр обещает фильтрацию, не делает её, и все шесть вызовов исправно его
передают, создавая ложное впечатление, что раскрытость учтена. Фильтрация — обязанность
поставщика строк; параметр должен уйти. (В `ClayGridExcelGenerator` одноимённый параметр —
рабочий, он сворачивает Excel Outline. Его не трогать.)

## Изменить/создать

### 1. Печать «Текущая страница» — строки берём с экрана, БД не трогаем

`ClayGrid.Dynamic.Export.cs`:

```csharp
/// <summary>
/// Печатная форма текущей страницы. Строки — ровно те, что на экране (Items):
/// свёрнутая группа печатается одной строкой-заголовком, раскрытая — тем куском,
/// который виден на странице. Догрузки нет: печать обязана совпадать с экраном
/// (для Excel действует другое правило — см. BuildDynamicExportRowsForCurrentPage).
/// </summary>
private Task<string> BuildDynamicPrintHtmlForCurrentPage(
    IReadOnlyList<ClayColumnMeta> columns, string? filterDescription, string? groupDescription)
    => Task.FromResult(ClayGridPrintHtmlGenerator.Build(
        Title, columns, (Items ?? []).OfType<IClayGridRow>().ToList(), CreateDynamicCellReader(),
        filterDescription, groupDescription));
```

Метод перестаёт быть `async` — как и статический близнец `BuildPrintHtmlForCurrentPageAsync`.
Точка вызова в `ClayGrid.ExportMenu.cs` не меняется (`await` над `Task<string>` работает).

`BuildPrintHtmlForCurrentPageAsync` (статика) — **не трогать**, он уже отдаёт `_rows`. Только
дописать в `///` одну фразу: строки — ровно экран, это требование, а не случайность.

`BuildDynamicExportRowsForCurrentPage` и `BuildExportRows` (статика) остаются как есть — теперь
у них единственный потребитель — Excel. Поправь их XML-doc: «только для Excel; печать берёт
строки с экрана».

### 2. Печать «Все данные» — весь список раскрытым

Динамика (`BuildDynamicPrintHtmlForAll` → `BuildDynamicExportRowsForAll`) уже соответствует
требованию — не трогать.

Статика, `ClayGridPageBase.cs`, `BuildPrintHtmlAsync`: заменить источник строк
`BuildAllRowsForPrint()` → на общий билдер «всех данных», который уже игнорирует раскрытость
(тот, что зовёт Excel).

`ClayGridPageBase.Export.Print.cs`: `BuildAllRowsForPrint` и `BuildAllGroupedRowsForPrint`
осиротеют нашей правкой — **удалить обе**. `BuildAllFlatRowsForPrint` остаётся: его зовёт
плоская ветка общего билдера.

Раз методы стали общими для печати и Excel, привести имена в соответствие (private, вызовов
мало — рефакторинг механический):

| Было | Стало |
|---|---|
| `BuildAllRowsForExcel` | `BuildAllRowsForExport` |
| `BuildAllGroupedRowsForExcel` | `BuildAllGroupedRowsForExport` |
| `BuildAllFlatRowsForPrint` | `BuildAllFlatRowsForExport` |

Файлы **не переименовывай** (`ClayGridPageBase.Export.Print.cs` теперь держит билдер, общий
с Excel) — напиши про эту кривизну в отчёте, решение по раскладке файлов принимает заказчик.
XML-doc переименованных методов — обновить: «печать (все данные) и Excel; раскрытость групп
игнорируется, выгружается весь список».

### 3. `ClayGridPrintHtmlGenerator` — убрать мёртвый параметр

Из обеих перегрузок `Build` удалить `HashSet<string>? expandedGroups = null` и передачу его
между перегрузками. Обновить все вызовы (их шесть):

- `ClayGrid.Dynamic.Export.cs` — `BuildDynamicPrintHtmlForCurrentPage`, `…ForAll`, `…ForSelected`;
- `ClayGridPageBase.Export.Excel.cs` — `BuildPrintHtmlForCurrentPageAsync`;
- `ClayGridPageBase.cs` — `BuildPrintHtmlAsync`;
- `ClayGridPageBase.Export.Selected.cs` — `BuildPrintHtmlForSelectedAsync`.

Свойство `DynamicExpandedGroups` в `ClayGrid.Dynamic.Export.cs` **остаётся** — его по-прежнему
берёт `ClayGridExcelGenerator.ExportToExcel`. Поправь его `///`: только для Excel Outline.

Проверь после правки: `grep -rn "expandedGroups" src` → попадания только в
`ClayGridExcelGenerator.cs` и в вызовах Excel.

## Не делай

- **Не трогай Excel** — ни `DynamicExcelExportAsync`, ни `ExcelExportAsync`, ни
  `ClayGridExcelGenerator`, ни его параметр `expandedGroups`, ни билдеры строк для Excel
  (`BuildDynamicExportRowsForCurrentPage`, `BuildExportRows`, `BuildAllRowsFor*`,
  `BuildAllRowsForSelected`). GB2/GB6 приняты, поведение Excel заказчиком подтверждено.
- Не откатывай GB2/GB6. Дефект не в них: они чинили Excel и заодно потащили за собой печать,
  потому что билдер был общий. GB10 разводит потребителей, а не отменяет фикс.
- Не «улучшай» печать текущей страницы догрузкой хвоста разорванной группы — заказчик выбрал
  строгий экран.
- Не фильтруй строки по `expandedGroups` внутри генератора печати — фильтрация делается
  поставщиком строк, генератор печатает то, что дали.
- Не трогай печать «Выбранные» (`BuildAllRowsForSelected`, `BuildDynamicExportRowsForSelected`) —
  кроме удаления мёртвого аргумента в вызове генератора.
- Не меняй `ClayGroupingEngine`.

## Проверка (ручная)

Стенды: `Kesco.App.Web.Inventory` `?id=140` (динамика) и `/medical-tests` (статика). Каждый
пункт — в ОБОИХ режимах, поведение обязано совпадать.

**Печать → Текущая страница:**
- группировка ВКЛ, все группы свёрнуты → в печатной форме только строки-заголовки групп со
  счётчиками «(N шт.)», детальных строк НЕТ — форма совпадает с экраном один в один;
- одна группа раскрыта → в форме её строки и заголовки остальных (свёрнутых) групп;
- раскрытая группа разорвана пагинацией → в форме только видимая часть, ровно как на экране;
- без группировки → строки текущей страницы;
- в SQL-профайлере: печать текущей страницы **не делает ни одного запроса** (строки берутся
  из `Items`/`_rows`).

**Печать → Все данные:**
- часть групп свёрнута → в форме ВЕСЬ список, все группы раскрыты, заголовки на местах,
  счётчики верные;
- три уровня группировки, все свёрнуты → в форме все три уровня заголовков и все строки;
- группа «(пусто)» (NULL в ключе) → её строки в форме есть;
- без группировки → все строки без пагинации;
- поиск + фильтр активны → печатается только отфильтрованное.

**Печать → Выбранные (N):**
- отметить записи в двух группах → в форме только они, под заголовками своих групп,
  счётчик в заголовке — полный размер группы (как было).

**Excel (регрессия, ничего не должно измениться):**
- «Текущая страница» при свёрнутых группах → данные в файле есть, узлы Outline свёрнуты;
- «Все данные», «Выбранные (N)» → как после GB2/GB6.

**Общее:**
- `grep -rn "expandedGroups" src` → только Excel;
- `grep -rn "BuildAllRowsForPrint\|BuildAllGroupedRowsForPrint" src` → пусто;
- `dotnet build Clayzor.sln` + `dotnet test tests\Clayzor.Lib.Web.Controls.Tests` — зелёные.
