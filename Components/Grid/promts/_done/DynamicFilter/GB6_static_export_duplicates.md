> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md**. Требует выполненного **GB2** (динамический режим приводится к тому же алгоритму — сверяйся с ним). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB6 — статический режим: дубли строк в экспорте «Текущая страница» при вложенной группировке

Прочитать перед началом: `Components/Grid/ClayGridPageBase.Export.Excel.cs` — `BuildExportRows()`
целиком (обе ветки: листовая группа и промежуточная), `CollectCounts`, `BuildAllGroupedRowsForExcel`;
`Components/Grid/ClayGridPageBase.cs` — `LoadGroupedData` (как заполняются `_rows`, `_groupTreeRoots`,
`_query.ExpandedGroups`), `_propertyMap`; `Components/Grid/ClayGroupingEngine.cs` —
`BuildGroupKeyWhere`, `BuildInterleavedHeaders`, `ComputeParentCounts`;
`Components/Grid/ClayGrid.Dynamic.Export.cs` — `BuildDynamicExportRowsForCurrentPage` **после GB2**
(эталон алгоритма) и `IsCoveredByExportedSubtree`;
`Components/Grid/ClayGridPageBase.Export.Print.cs` — кто ещё зовёт `BuildExportRows`.

## Дефект

Найден при разборе GB2. Заявки от тестировщика на него не было — дефект воспроизводится только
при вложенной группировке (два уровня и больше) и потому не попался.

`BuildExportRows()` идёт по `_rows` текущей страницы и на КАЖДЫЙ `GroupHeaderRow` грузит данные:

- листовой заголовок (`header.GroupKeys.Count == groupCols.Count`) → заголовок + все его строки;
- промежуточный заголовок → GROUP BY по поддереву + `SELECT *` по поддереву + C#-интерливинг
  вложенных заголовков.

Ветка промежуточной группы **не проверяет, не лежат ли её потомки тут же, на странице**.
Раскрытая группа 1-го уровня даёт в `_rows` и себя, и свои дочерние заголовки, и детали.
В результате:

1. родительский заголовок грузит ВСЁ поддерево (вложенные заголовки + строки);
2. следом каждый дочерний заголовок из `_rows` грузит свои строки ещё раз.

→ **строки и заголовки задваиваются**, счётчики в заголовках при этом верные, из-за чего файл
выглядит правдоподобно. Чем глубже группировка, тем больше копий.

Второй дефект в той же ветке — `NULL`-ключ:

```csharp
partialKeyWhere.Add($"{groupCols[i]} = @pk{i}");
...
subtreeParams.Add($"pk{i}", header.GroupKeys[i]);
```

После GN2 `GroupKeys[i] == ""` означает `NULL` в данных. `col = @pk0` при пустой строке не найдёт
ничего (а на nullable-колонке `= NULL` не истинно никогда) → **группа «(пусто)» промежуточного
уровня выгружается пустой**. В листовой ветке это уже починено через
`ClayGroupingEngine.BuildGroupKeyWhere` (GN3), в промежуточной — нет.

Третье, попутно: на каждый промежуточный заголовок делается свой агрегатный запрос ради
`countLookup`, хотя дерево групп текущего запроса уже лежит в `_groupTreeRoots`
(`ComputeParentCounts` вызван в `LoadGroupedData`).

## Изменить/создать

`Components/Grid/ClayGridPageBase.Export.Excel.cs`, `BuildExportRows()` — заменить обе ветки
одним алгоритмом, симметричным динамическому (GB2): заголовок → поддерево одним запросом →
вложенные заголовки интерливингом; заголовки, чьё поддерево уже выгружено вместе с предком,
пропускаются.

```csharp
/// <summary>
/// Строит список строк для экспорта текущей страницы. Если активна группировка —
/// для каждого заголовка группы на странице загружает ВСЕ строки её поддерева
/// (игнорируя пагинацию и раскрытость). Заголовок, поддерево которого уже выгружено
/// вместе с его предком, пропускается — иначе строки задваиваются.
/// На грид не влияет: данные грузятся отдельным запросом.
/// </summary>
private async Task<List<IClayGridRow>> BuildExportRows()
{
    if (!_query.GroupEnabled || _query.GroupColumns.Count == 0)
        return _rows;

    var selectSql     = Grid?.SelectSql     ?? "";
    var defaultOrder  = Grid?.DefaultOrder  ?? "";
    var searchColumns = Grid?.SearchColumns ?? [];

    var searchWhere    = _query.BuildWhereClause(searchColumns);
    var dp             = new DynamicParameters();
    dp.Add("search", $"%{_query.SearchText}%");
    var compositeWhere = BuildCompositeFilterClause(_query.CompositeFilter, dp);
    var where          = ClayDataQuery.CombineWhere(searchWhere, compositeWhere);

    var groupCols = _query.GroupColumns.ToList();
    // BuildOrderBy при группировке начинается с группировочных колонок — интерливинг
    // вложенных заголовков требует именно такого порядка (НЕ BuildDetailOrder).
    var orderBy   = _query.BuildOrderBy(defaultOrder);

    // Счётчики — из дерева последней загрузки. Агрегатные запросы на каждый заголовок не нужны.
    var countLookup = new Dictionary<string, int>();
    if (_groupTreeRoots is not null)
        CollectCounts(_groupTreeRoots, countLookup);

    var result  = new List<IClayGridRow>();
    var covered = new List<string>();

    foreach (var row in _rows)
    {
        if (row is not GroupHeaderRow header) continue;
        if (covered.Any(k => header.FullKey == k || header.FullKey.StartsWith(k + '\u001F'))) continue;

        result.Add(header);
        covered.Add(header.FullKey);

        var detailParams = new DynamicParameters();
        detailParams.AddDynamicParams(dp);

        // GroupKeys — строки; после GN2 "" означает NULL-ключ → null для IS NULL (GN3).
        // Ключей может быть меньше числа уровней: тогда это WHERE по поддереву.
        var rawKeys  = header.GroupKeys.Select(k => k.Length == 0 ? null : (object?)k).ToList();
        var keyWhere = ClayGroupingEngine.BuildGroupKeyWhere(groupCols, rawKeys, "dk", out var keyParams);
        foreach (var (name, value) in keyParams)
            detailParams.Add(name, value);

        var detailWhere = keyWhere.Length > 0
            ? ClayDataQuery.CombineWhere(where, keyWhere)
            : where;

        var sql = $"SELECT * FROM ({selectSql}) _src";
        if (!string.IsNullOrWhiteSpace(detailWhere)) sql += $" WHERE {detailWhere}";
        if (!string.IsNullOrWhiteSpace(orderBy))     sql += $" ORDER BY {orderBy}";

        var items = await Db.QueryAsync<T>(sql, detailParams);

        // previousKeys стартует с ключей заголовка — сам заголовок не продублируется,
        // вставятся только уровни ниже.
        IReadOnlyList<string?>? previousKeys = header.GroupKeys;

        foreach (var item in items)
        {
            var currentKeys = groupCols
                .Select(c => _propertyMap.TryGetValue(c, out var p) ? p.GetValue(item)?.ToString() : null)
                .ToArray();

            result.AddRange(ClayGroupingEngine.BuildInterleavedHeaders(currentKeys, previousKeys, countLookup));
            result.Add(new DetailRow<T> { Item = item });
            previousKeys = currentKeys;
        }
    }

    return result;
}
```

Отличие от динамического близнеца ровно в трёх местах: источник строк (`Db.QueryAsync<T>` вместо
`DynamicSql.QueryRowsAsync`), обёртка (`DetailRow<T>` вместо `ClayDynamicRow`) и чтение ключа
(`_propertyMap` вместо словаря). Подмены `ResolveGroupDisplayValue` здесь нет — она нужна только
динамическим Типам 5/9.

Что осиротеет нашей правкой и должно уйти из метода: `detailOrder`/`BuildDetailOrder`, локальные
`countLookup` внутри веток, per-header агрегатные запросы (`subtreeGroupSql`, `subtreeParams`,
`subtreeAggregates`, `subtreeRoots`), параметры `pk{i}`, поле `Depth` у `DetailRow<T>` в этом
методе (интерливинг его не проставляет — как и в `BuildAllGroupedRowsForExcel`).
`CollectCounts` остаётся: её зовёт `BuildAllGroupedRowsForExcel`.

Если `IsCoveredByExportedSubtree` из GB2 после этого дублируется дословно — **оставь две копии**
(разные классы, `ClayGrid<T>` и `ClayGridPageBase<T>`); выносить общий хелпер в
`ClayGroupingEngine` ради четырёх строк не надо (Simplicity First). Если решишь иначе — обоснуй
в отчёте, а не молча.

## Не делай

- Не трогай `BuildAllGroupedRowsForExcel` и `BuildAllFlatRowsForPrint` — там уже один запрос
  на всё и дублей нет.
- Не трогай `BuildAllRowsForSelected` (`ClayGridPageBase.Export.Selected.cs`) — свой алгоритм,
  свои проверки.
- Не трогай `LoadGroupedData` и `_rows` — грид рисуется правильно, дефект только в экспорте.
- Не меняй `ClayGroupingEngine`.
- Не «оптимизируй» до одного запроса на всю страницу с OR-ами по ключам — как и в GB2.
- Не выравнивай сигнатуры/структуру `ClayGridPageBase.Export.*` и `ClayGrid.Dynamic.Export.cs`
  «до общего кода» — это отдельный рефакторинг, не багфикс.

## Проверка (ручная)

Стенд: `/medical-tests`, группировка через трей.

- **два уровня группировки, верхняя группа РАСКРЫТА** (главный сценарий дефекта) →
  «Групповые операции» → «Выгрузка в Excel» → «Текущая страница»: каждая строка в файле ровно
  один раз, каждый заголовок группы ровно один раз; число строк = сумме счётчиков групп
  верхнего уровня, попавших на страницу;
- то же с тремя уровнями и раскрытыми первым и вторым;
- все группы свёрнуты → в файле есть и заголовки, и все строки (регрессия GB2-логики
  в статике), дублей нет;
- промежуточная группа с `NULL` в ключе («(пусто)») → её строки в файле есть (`IS NULL`);
- одна колонка группировки → поведение как до фикса;
- «Печать» → «Текущая страница» (тот же `BuildExportRows`) → дублей нет;
- «Все данные», «Выбранные (N)» → как до фикса;
- без группировки → строки страницы, как до фикса;
- поиск + фильтр активны → в файле только отфильтрованное;
- в SQL-профайлере: на страницу с N «корневыми» заголовками — N запросов деталей и НИ ОДНОГО
  дополнительного агрегатного (счётчики из `_groupTreeRoots`);
- динамический режим (`?id=140`) — не задет, работает как после GB2;
- `dotnet test tests\Clayzor.Lib.Web.Controls.Tests` — зелёный.
