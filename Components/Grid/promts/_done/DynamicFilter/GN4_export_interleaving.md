> Часть плана «Группировка на произвольное число уровней». Перед началом прочитай **GN0_README_grouping_levels.md** и **_readme_grid_dynamic.md**. Требует выполненных **GN2**, **GN3**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GN4 — C#-interleaving экспорта: N уровней и согласование с движком

Завершающий шаг плана. Здесь заголовки групп в выгрузке начинают совпадать с тем, что показывает
грид, и из кода уходят последние упоминания про «до 3-х уровней».

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGridPageBase.Export.Excel.cs` → `BuildAllGroupedRowsForExcel`, блок
  «C# interleaving».
- `Components/Grid/ClayGridPageBase.Export.Selected.cs` → `BuildAllGroupedRowsForSelected`,
  такой же блок. **Их два, и они почти идентичны.**
- `Components/Grid/ClayGrid.Dynamic.Export.cs` → `BuildDynamicGroupedExportRows` (**если есть**, GE3) —
  третий такой же блок.
- `Components/Grid/ClayGroupingEngine.cs` — `BuildAggregates` после GN2, константа
  `EmptyGroupDisplay`, формат `FullKey`.
- `Components/Grid/ClayGridPageBase.cs` — `_propertyMap` (используется interleaving-ом).

## Задача

Экспорт «все данные» и «выбранные» с группировкой не гоняет по запросу на каждую группу, а
делает два запроса (агрегат + плоский список) и расставляет заголовки групп в C# —
однопроходным детектированием смены ключа. Логика продублирована в двух (или трёх) местах:

```csharp
var currentKeys = groupCols
    .Select(c => _propertyMap.TryGetValue(c, out var p) ? p.GetValue(item)?.ToString() : null)
    .ToArray();

int firstDiff = 0;
if (previousKeys is not null)
    while (firstDiff < previousKeys.Length
           && firstDiff < currentKeys.Length
           && string.Equals(previousKeys[firstDiff], currentKeys[firstDiff]))
        firstDiff++;

for (int depth = firstDiff; depth < groupCols.Count; depth++)
{
    var keys         = currentKeys.Take(depth + 1).ToList();
    var displayValue = keys[depth] ?? "(пусто)";
    var fullKey      = string.Join("\u001F", keys);
    result.Add(new GroupHeaderRow { DisplayValue = displayValue, FullKey = fullKey,
        ItemCount = countLookup.TryGetValue(fullKey, out var cnt) ? cnt : 0,
        Depth = depth, GroupKeys = keys! });
}
```

Хорошая новость: **сам цикл `for depth in firstDiff..groupCols.Count` уже работает на любое
число уровней.** Потолка здесь никогда не было — он был в `BuildAggregates` (GN2) и в SQL (GN1).
Плохая: после GN2 формат ключей у движка и у interleaving-а разошёлся в деталях, а `countLookup`
ищет по `FullKey` строгим совпадением. Разошлись → счётчики групп в выгрузке станут нулями.

Три расхождения:

1. **`null` vs `""`.** Interleaving держит `string?[]` с `null`, а `string.Join` превращает
   `null` в пустую строку → `FullKey` совпадает с движковым. **Здесь повезло, менять не надо.**
   Но `previousKeys[i]` сравнивается через `string.Equals(null, null)` → `true`, а движок
   работает со строками `""`. Поведение совпадает, но неявно — фиксируем нормализацией.
2. **`"(пусто)"` — литерал в двух местах.** GN2 ввёл `ClayGroupingEngine.EmptyGroupDisplay`.
   Литерал обязан уехать: разъедутся — подписи групп в гриде и в файле станут разными.
3. **`countLookup` собирается из дерева (`CollectCounts`)**, то есть ключи там движковые.
   Если interleaving построит `FullKey` иначе хоть на один символ — `TryGetValue` не найдёт,
   и в файле будут «(0 шт.)» у всех групп. Это единственная связь между двумя ветками, и она
   держится на совпадении формата.

## Изменить/создать

**1.** `ClayGroupingEngine.cs` — вынести построение заголовков в движок, чтобы формат был
в одном месте:

```csharp
    /// <summary>
    /// Строит заголовки групп, которые нужно вставить перед строкой с ключами
    /// <paramref name="currentKeys"/>, при однопроходном обходе отсортированных строк
    /// (C#-interleaving в экспорте). Число уровней не ограничено.
    /// Формат FullKey и подписи «(пусто)» совпадает с <see cref="BuildAggregates"/> —
    /// от этого зависит поиск в countLookup.
    /// </summary>
    /// <param name="currentKeys">Строковые ключи текущей строки по уровням (null → «нет значения»).</param>
    /// <param name="previousKeys">Ключи предыдущей строки или null для первой строки.</param>
    /// <param name="countLookup">FullKey → ItemCount из дерева агрегатов.</param>
    /// <returns>Заголовки от самого внешнего сменившегося уровня до самого глубокого.</returns>
    public static List<GroupHeaderRow> BuildInterleavedHeaders(
        IReadOnlyList<string?> currentKeys,
        IReadOnlyList<string?>? previousKeys,
        IReadOnlyDictionary<string, int> countLookup)
    {
        var headers = new List<GroupHeaderRow>();

        int firstDiff = 0;
        if (previousKeys is not null)
            while (firstDiff < previousKeys.Count
                   && firstDiff < currentKeys.Count
                   && string.Equals(previousKeys[firstDiff] ?? "", currentKeys[firstDiff] ?? ""))
                firstDiff++;

        for (int depth = firstDiff; depth < currentKeys.Count; depth++)
        {
            var keys    = currentKeys.Take(depth + 1).Select(k => k ?? "").ToList();
            var fullKey = string.Join("\u001F", keys);

            headers.Add(new GroupHeaderRow
            {
                DisplayValue = keys[depth].Length > 0 ? keys[depth] : EmptyGroupDisplay,
                FullKey      = fullKey,
                ItemCount    = countLookup.GetValueOrDefault(fullKey),
                Depth        = depth,
                GroupKeys    = keys,
            });
        }

        return headers;
    }
```

Что изменилось относительно копипасты:

- `?? ""` при сравнении и при сборке ключей — нормализация `null` к тому же виду, что в
  `BuildAggregates` (там `k?.ToString() ?? ""`). Раньше совпадение было случайным;
- `"(пусто)"` → `EmptyGroupDisplay` (GN2);
- `GroupKeys` — `List<string>` без `!`-подавления: после нормализации там нет `null`;
- `IsExpanded` не выставляется — его нет и в оригинале: в экспорте раскрытость приходит
  отдельным параметром `expandedGroups` генератора.

**2.** `ClayGridPageBase.Export.Excel.cs` → `BuildAllGroupedRowsForExcel` — заменить блок:

```csharp
        var result = new List<IClayGridRow>();
        string?[]? previousKeys = null;

        foreach (var item in items)
        {
            var currentKeys = groupCols
                .Select(c => _propertyMap.TryGetValue(c, out var p) ? p.GetValue(item)?.ToString() : null)
                .ToArray();

            result.AddRange(ClayGroupingEngine.BuildInterleavedHeaders(currentKeys, previousKeys, countLookup));
            result.Add(new DetailRow<T> { Item = item });
            previousKeys = currentKeys;
        }

        return result;
```

**3.** `ClayGridPageBase.Export.Selected.cs` → `BuildAllGroupedRowsForSelected` — тот же
блок, та же замена. Обрати внимание: там `result.Add(new DetailRow<T> { Item = item });`
без `Depth` — так и оставь, не «унифицируй» с печатью.

**4.** `ClayGrid.Dynamic.Export.cs` → `BuildDynamicGroupedExportRows` (если есть, GE3):

```csharp
        foreach (var raw in rawRows)
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
```

Если `ResolveGroupDisplayValue` нет (GG6 не выполнен) — просто `result.AddRange(...)` без
подмены и скажи об этом в отчёте.

**5.** Вычистить упоминания потолка. `grep -rni "до 3-х уровней\|до трёх уровней\|K0–K2\|K0-K2\|3-х уровней"`
по `--include=*.cs --include=*.md`. Ожидаемые места:

- xml-doc `GridGroupRow` — «Поля K0–K2 … (до 3-х уровней)». После GN1 полей нет; если
  формулировка выжила — убрать;
- xml-doc `BuildGroupAggregateSql` — «Возвращает до 3-х ключевых колонок K0–K2». GN1 её
  переписал; проверь;
- `GG0_README_dynamic_grouping.md`, раздел «Ограничения, которые НЕ надо чинить в этом плане» —
  **перепиши**: ограничения больше нет, оно снято планом GN. Оставь ссылку на GN0;
- `GF7_backlog.md`, пункт 1а — отметь как реализованный планом GN;
- `promts/G04_dynamic_render.md` и другие исходные промты — **НЕ трогай**: это история, а не
  действующая документация.

## Не делай

Не трогай `BuildAggregates` (GN2), `BuildGroupKeyWhere` (GN3), `BuildTree`, `WalkTree`,
`ComputeEffectiveRows`, `ComputeParentCounts`. Не трогай `BuildAllGroupedRowsForPrint` — он
строит детали отдельным запросом на группу и interleaving не использует (это осознанное
отличие печати от Excel, см. xml-doc `BuildAllGroupedRowsForExcel`). Не объединяй печатную и
excel-ветки. Не меняй разделитель `\u001F` и не трогай `CollectCounts`. Не вводи лимит уровней.

## Проверка

**Юнит (`BuildInterleavedHeaders`) — БД не нужна:**

- первая строка, `previousKeys = null`, `currentKeys = ["a","b"]` → два заголовка: `Depth 0`
  `FullKey "a"`, `Depth 1` `FullKey "a\u001Fb"`;
- та же группа подряд: `previous = ["a","b"]`, `current = ["a","b"]` → **пусто**;
- сменился внутренний: `previous = ["a","b"]`, `current = ["a","c"]` → ОДИН заголовок,
  `Depth 1`, `FullKey "a\u001Fc"`;
- сменился внешний: `previous = ["a","b"]`, `current = ["x","y"]` → ДВА заголовка (`Depth 0`, `Depth 1`);
- **пять уровней**, сменился третий → три заголовка (`Depth 2,3,4`);
- `currentKeys = ["a", null]` → `Depth 1`, `DisplayValue == ClayGroupingEngine.EmptyGroupDisplay`,
  `FullKey == "a\u001F"`;
- `previous = ["a", null]`, `current = ["a", null]` → пусто (`null` и `null` — одна группа);
- `previous = ["a", null]`, `current = ["a", ""]` → **пусто** (пустая строка и NULL неразличимы
  на этом уровне — известное ограничение, см. GN3);
- `countLookup` не содержит ключа → `ItemCount == 0`, исключения нет;
- `GroupKeys` не содержит `null` ни при каких входных данных.

**Согласованность движка и interleaving — главный тест шага:**

- возьми набор `[{Keys=["a","b","c1"],Cnt=2}, {Keys=["a","b","c2"],Cnt=3}, {Keys=["a","b2","c3"],Cnt=1}]`;
- прогони `BuildAggregates` → `BuildTree` → `ComputeParentCounts` → `CollectCounts` → `countLookup`;
- прогони `BuildInterleavedHeaders` по строкам `["a","b","c1"]`, `["a","b","c2"]`, `["a","b2","c3"]`;
- **каждый полученный `FullKey` обязан находиться в `countLookup`**, а `ItemCount` заголовков —
  совпадать со значениями из дерева (`"a"` = 6, `"a\u001Fb"` = 5, `"a\u001Fb\u001Fc1"` = 2);
- повторить с `null` в ключах — совпадение обязано сохраниться.

**Ручная.** Статический режим (`MedicalTests.razor`):

- группировка по трём колонкам, «Excel → Все данные» → в файле трёхуровневые заголовки,
  **счётчики не нулевые** и совпадают с гридом, вложенный Outline сворачивается;
- пять колонок → пять уровней Outline;
- колонка с `NULL` → в файле группа «(пусто)» с верным счётчиком, под ней её строки (GN3);
- «Excel → Выбранные (N)» с группировкой на трёх уровнях → заголовки только тех групп, где
  есть выбранные, счётчик — ПОЛНЫЙ размер группы;
- «Печать → Все данные» с тремя уровнями → дерево верное (печать идёт другим путём, но
  `BuildAggregates` общий — это проверка GN2 ещё раз);
- **сверь глазами**: подпись группы в гриде и в файле — одна и та же строка, включая «(пусто)».

Динамический режим (если сделаны GG7 / GE6): то же, плюс заголовки групп Тип 5 показывают
наименования и в гриде, и в файле (GG6).

Регрессия: 1–2 уровня без `NULL` — файлы совпадают с эталоном, снятым до GN1.
