> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md**, **GE0_README_dynamic_export.md** и **GG0_README_dynamic_grouping.md**. Требует выполненных **GE3**, **GG1–GG8**, **GN1–GN4**, **GB1**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB2 — в Excel и печать «Текущая страница» не попадают данные свёрнутых групп

Прочитать перед началом: `Components/Grid/ClayGrid.Dynamic.Export.cs` — целиком
(`BuildDynamicExportWhere`, `BuildDynamicSelectAllSql`, `BuildDynamicExportRowsForCurrentPage`,
`BuildDynamicGroupedExportRows`, `CollectDynamicGroupCounts`, `DynamicExcelExportAsync`);
`Components/Grid/ClayGrid.Dynamic.Grouping.cs` — `_dynamicExpandedGroups`, `_dynamicGroupRoots`,
`LoadDynamicGroupedData` (особенно как считается `layout` и как грузятся детали),
`ResolveGroupDisplayValue`; `Components/Grid/ClayGroupingEngine.cs` — `BuildGroupKeyWhere`,
`BuildInterleavedHeaders`, `BuildDetailOrder`, `ComputeParentCounts`, `BuildTree`;
`Components/Grid/ClayGridPageBase.Export.Excel.cs` — `BuildExportRows` (статический аналог,
как там сделаны листовые и промежуточные группы) и `BuildAllGroupedRowsForExcel`;
`Components/Grid/ClayGridRow.cs` — `GroupHeaderRow` (`FullKey`, `GroupKeys`, `Depth`, `ItemCount`);
`Services/ClayGridExcelGenerator.cs` — как используется `expandedGroups` (Excel Outline).

## Дефект

`BuildDynamicExportRowsForCurrentPage` (используется и печатью, и Excel для режима
«Текущая страница») содержит два `continue`, из-за которых у группы остаётся заголовок без строк:

```csharp
foreach (var row in Items ?? [])
{
    if (row is not GroupHeaderRow gh)
        continue;                       // детали текущей страницы перезагрузим целиком

    result.Add(gh);

    if (!_dynamicExpandedGroups.Contains(gh.FullKey)) continue;   // ← дефект 1
    if (gh.GroupKeys.Count != exprs.Count) continue;              // ← дефект 2
    ...
}
```

**Дефект 1.** Свёрнутая группа выгружается пустой. Ровно то, на что жалуется тестировщик:
«при экспорте в Excel не подгружаются данные закрытых групп». Раскрытость группы — состояние
ЭКРАНА (сколько строк влезло на страницу), а не признак «этих данных не надо». Статический
режим (`ClayGridPageBase.BuildExportRows`) `ExpandedGroups` при сборке строк не смотрит вообще
и грузит детали для каждого заголовка на странице — по нему и равняемся. Про то, что группа
свёрнута, экспорт всё равно узнает: `expandedGroups` отдельно передаётся в
`ClayGridExcelGenerator.ExportToExcel`, который сворачивает соответствующие строки Excel Outline.
**Данные в файле есть, узел просто свёрнут** — это и есть требуемое поведение, менять его не надо.

**Дефект 2.** Промежуточный (не листовой) заголовок группы выгружается пустым. Комментарий
«промежуточный уровень — детали ниже» верен только когда группа раскрыта и её потомки попали
на ту же страницу. Свёрнутый заголовок первого уровня при трёх уровнях группировки не даст
ни строк, ни вложенных заголовков.

**Третий, скрытый дефект — дубли.** Если чинить в лоб (снять оба `continue` и грузить поддерево
на каждый заголовок), у раскрытой группы поддерево загрузится и на родительском заголовке,
и на каждом дочернем → строки задвоятся. Ровно эта мина сейчас лежит в статическом
`BuildExportRows` (ветка промежуточной группы не проверяет, не обработаны ли её потомки
отдельно). **В этом шаге чиним только динамический режим**; про находку в статическом —
одна строка в отчёте, кода не трогать (`/AGENTS.md`, Surgical Changes).

Побочная выгода: агрегатный запрос для счётчиков не нужен. `_dynamicGroupRoots` — дерево групп
последней загрузки со всеми уровнями и посчитанными `ItemCount` (`ComputeParentCounts` вызван
в `LoadDynamicGroupedData`). Счётчики берём из него.

## Изменить/создать

`Components/Grid/ClayGrid.Dynamic.Export.cs`, `BuildDynamicExportRowsForCurrentPage` —
переписать целиком:

```csharp
/// <summary>
/// Строки текущей страницы. Без группировки — то, что уже в Items.
/// С группировкой — для каждого заголовка группы на странице догружаются ВСЕ строки её
/// поддерева, независимо от пагинации и от того, раскрыта группа или свёрнута:
/// раскрытость — состояние экрана, а не признак «этих данных не надо». Свёрнутость
/// доезжает до Excel отдельно — через expandedGroups и Excel Outline.
/// Заголовок, поддерево которого уже выгружено вместе с его предком, пропускается —
/// иначе строки задвоятся.
/// </summary>
public async Task<List<IClayGridRow>> BuildDynamicExportRowsForCurrentPage()
{
    var query = _lastQuery;
    if (!query.GroupEnabled || query.GroupColumns.Count == 0)
        return (Items ?? []).OfType<IClayGridRow>().ToList();

    var (where, dp) = BuildDynamicExportWhere();
    var exprs       = query.GroupColumns.ToList();
    // ORDER BY с группировкой начинается с группировочных колонок (ClayDataQuery.BuildOrderBy),
    // а BuildInterleavedHeaders требует строки, отсортированные по уровням. Поэтому здесь
    // полный orderBy, а НЕ BuildDetailOrder (тот выбрасывает группировочные колонки).
    var orderBy     = query.BuildOrderBy(DefaultOrder);

    // Счётчики групп — из дерева последней загрузки (ComputeParentCounts уже вызван
    // в LoadDynamicGroupedData). Второй агрегатный запрос не нужен.
    var countLookup = new Dictionary<string, int>();
    if (_dynamicGroupRoots is not null)
        CollectDynamicGroupCounts(_dynamicGroupRoots, countLookup);

    var result  = new List<IClayGridRow>();
    var covered = new List<string>();   // FullKey заголовков, поддеревья которых уже выгружены

    foreach (var row in Items ?? [])
    {
        if (row is not GroupHeaderRow gh) continue;
        if (IsCoveredByExportedSubtree(gh.FullKey, covered)) continue;

        result.Add(gh);
        covered.Add(gh.FullKey);

        var detailParams = new DynamicParameters();
        detailParams.AddDynamicParams(dp);

        // GroupKeys — строки; после GN2 "" означает NULL-ключ → null для IS NULL (GN3).
        // Ключей может быть меньше, чем уровней: у промежуточного заголовка это даёт
        // WHERE по поддереву, а не по листовой группе.
        var rawKeys  = gh.GroupKeys.Select(k => k.Length == 0 ? null : (object?)k).ToList();
        var keyWhere = ClayGroupingEngine.BuildGroupKeyWhere(exprs, rawKeys, "dk", out var keyParams);
        foreach (var (name, value) in keyParams)
            detailParams.Add(name, value);

        var detailWhere = keyWhere.Length > 0
            ? ClayDataQuery.CombineWhere(where, keyWhere)
            : where;

        var sql  = BuildDynamicSelectAllSql(detailWhere, orderBy);
        var rows = await DynamicSql.QueryRowsAsync(Db, sql, detailParams);

        // previousKeys стартует с ключей самого заголовка: BuildInterleavedHeaders сравнивает
        // поуровнево, поэтому уровни 0..gh.Depth совпадут и заголовок не продублируется —
        // вставятся только заголовки уровней ниже.
        IReadOnlyList<string?>? previousKeys = gh.GroupKeys;

        foreach (var raw in rows)
        {
            var currentKeys = exprs
                .Select(c => raw.TryGetValue(c, out var v) && v is not DBNull ? v?.ToString() : null)
                .ToArray();

            foreach (var header in ClayGroupingEngine.BuildInterleavedHeaders(currentKeys, previousKeys, countLookup))
            {
                // Тип 5/9: в ключе код, показать надо наименование (GG6)
                header.DisplayValue = ResolveGroupDisplayValue(exprs[header.Depth], header.DisplayValue);
                result.Add(header);
            }

            result.Add(new ClayDynamicRow(raw));
            previousKeys = currentKeys;
        }
    }

    return result;
}

/// <summary>
/// true — поддерево этого заголовка уже выгружено вместе с одним из его предков.
/// Сегменты FullKey разделены \u001F (см. ClayGroupingEngine).
/// </summary>
private static bool IsCoveredByExportedSubtree(string fullKey, List<string> covered)
    => covered.Any(k => fullKey == k || fullKey.StartsWith(k + '\u001F'));
```

Что стало ненужным в этом методе после правки — `detailOrder` / `BuildDetailOrder` и
проверка `_dynamicExpandedGroups`. Убери их из метода (`/AGENTS.md`: чистим орфанов,
созданных СВОИМИ изменениями). `DynamicExpandedGroups` как свойство **оставь** — оно по-прежнему
передаётся в генераторы печати и Excel.

`BuildDynamicExportRowsForAll` и `BuildDynamicExportRowsForSelected` **не трогай**: они идут
через `BuildDynamicGroupedExportRows`, который грузит все строки одним запросом и раскрытость
не смотрит — там дефекта нет.

## Не делай

- Не убирай `DynamicExpandedGroups` из вызовов `ClayGridExcelGenerator.ExportToExcel` и
  `ClayGridPrintHtmlGenerator.Build`. Свёрнутая в гриде группа обязана приехать в Excel
  свёрнутым узлом Outline — **но с данными внутри**. Это и есть цель фикса.
- Не трогай `ClayGroupingEngine` — `BuildGroupKeyWhere` уже умеет частичный набор ключей,
  `BuildInterleavedHeaders` уже умеет произвольное число уровней (GN).
- Не трогай `ClayGridPageBase.Export.Excel.BuildExportRows` (статический режим): заявленного
  дефекта там нет. Про найденную мину с дублями при вложенной группировке — строка в отчёте.
- Не заменяй N запросов «по заголовку на странице» на один общий запрос с OR-ами по ключам:
  экономия сомнительная, риск порчи порядка строк и интерливинга — большой. Simplicity First.
- Не подставляй значения ключей в текст SQL — только через `DynamicParameters` (префикс `dk`).
- Не меняй сигнатуры публичных `BuildDynamicExportRowsFor*` — на них завязаны GE4/GE5.

## Проверка (ручная)

Стенд: `Kesco.App.Web.Inventory`, `?id=140`, группировка включена (перетащить колонку в трей).

- одна колонка группировки, ВСЕ группы свёрнуты, «Групповые операции» → «Выгрузка в Excel» →
  «Текущая страница» → в файле у каждой группы есть заголовок И все её строки; узлы Outline
  свёрнуты (`+` слева), при раскрытии видны строки;
- сумма строк в файле = сумме счётчиков в заголовках групп на странице;
- одна группа раскрыта, остальные свёрнуты → в файле НЕТ дублей строк раскрытой группы,
  счётчик в её заголовке — полный размер группы, а не число строк на экране;
- группа, детали которой разорваны пагинацией (раскрыта, хвост ушёл на следующую страницу) →
  в файле группа целиком, один раз;
- две колонки группировки, верхний уровень свёрнут → в файле заголовок 1-го уровня, под ним
  заголовки 2-го уровня и строки, дублей нет;
- три уровня (GN снял потолок), верхний свёрнут → все три уровня заголовков + строки;
- группа «(пусто)» (NULL в колонке группировки) → строки выгрузились (`IS NULL`, не `= @dk`);
- то же самое для «Печать» → «Текущая страница»: у свёрнутых групп в печатной форме есть строки;
- Тип 5/9 в колонке группировки → в заголовках групп файла наименования, а не коды
  (`ResolveGroupDisplayValue`);
- поиск + фильтр активны → экспорт «Текущая страница» отдаёт только отфильтрованное
  (`BuildDynamicExportWhere` тот же);
- «Все данные» и «Выбранные (N)» — работают как до фикса (регрессия);
- без группировки (плоский режим) → «Текущая страница» отдаёт ровно строки страницы,
  как до фикса;
- статический режим (`/medical-tests`) — печать и Excel во всех трёх режимах работают как до
  фикса (кода не касались);
- в SQL-профайлере: на страницу с N заголовками — N запросов деталей, агрегатных запросов
  дополнительно НЕТ (счётчики из `_dynamicGroupRoots`).
