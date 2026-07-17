> Часть плана «Группировка на произвольное число уровней». Перед началом прочитай **GN0_README_grouping_levels.md** (особенно раздел «Что на самом деле сломано») и **_readme_grid_dynamic.md**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GN1 — `GridGroupRow.Keys`: список ключей вместо `K0/K1/K2`

Прочитать перед началом (обязательно, до написания кода):

- `Components/Grid/ClayGroupingEngine.cs` — `GridGroupRow`, `BuildGroupAggregateSql`.
  `BuildAggregates` — прочитай, но **не трогай, это GN2**.
- `Components/Grid/ClayGridPageBase.cs` — `LoadGroupedData`, строка
  `var groupRows = await Db.QueryAsync<GridGroupRow>(groupSql, dp);`.
- `Components/Grid/ClayGridPageBase.Export.Print.cs` — `BuildAllGroupedRowsForPrint`, та же строка.
- `Components/Grid/ClayGridPageBase.Export.Excel.cs` — `BuildAllGroupedRowsForExcel`, та же строка.
- `Components/Grid/ClayGridPageBase.Export.Selected.cs` — `BuildAllGroupedRowsForSelected`, та же строка.
- `Components/Grid/Dynamic/ClayDynamicGroupMapper.cs` — **если существует** (создан в GG1).
- `Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` — `QueryRowsAsync`.

**Первым делом выполни проверку совместимости из GN0:**

```
grep -rn "GridGroupRow" --include=*.cs --include=*.razor .
grep -rn "\.K0\|\.K1\|\.K2" --include=*.cs --include=*.razor .
```

Ожидаемые места: `ClayGroupingEngine.cs`, четыре `Db.QueryAsync<GridGroupRow>` в
`ClayGridPageBase*`, `ClayDynamicGroupMapper.cs` (если есть), тесты. **Нашёл что-то ещё —
ОСТАНОВИСЬ и покажи, не правь молча.**

## Задача

Сейчас число уровней жёстко зашито в трёх местах, и они не согласованы (см. GN0, «Факт 1»).
Снимаем потолок в двух из них: SQL отдаёт `K0..K{n-1}` по числу колонок, `GridGroupRow` хранит
список. `BuildAggregates` остаётся с двумя уровнями до GN2 — это осознанно: шаг должен
компилироваться и не менять текущее поведение для 1–2 уровней.

Побочный эффект шага: Dapper больше не может мапить агрегат на `GridGroupRow` автоматически
(имена колонок `K0`, `K1`, … переменные, а свойство одно — `Keys`). Значит статический режим
переходит на чтение словарей + общий маппер. Динамический уже так делает (GG1).

## Изменить/создать

**1.** `ClayGroupingEngine.cs` — `GridGroupRow`:

```csharp
/// <summary>
/// Строка результата агрегатного GROUP BY-запроса.
/// Число уровней не ограничено: <see cref="Keys"/> содержит ровно столько значений,
/// сколько колонок в группировке, в том же порядке.
/// </summary>
public class GridGroupRow
{
    /// <summary>
    /// Значения группировочных колонок по уровням: Keys[0] — внешний уровень.
    /// null — законное значение ключа (NULL в данных), а НЕ признак отсутствия уровня.
    /// </summary>
    public List<object?> Keys { get; set; } = [];

    /// <summary>Количество строк детализации в этой листовой группе.</summary>
    public int Cnt { get; set; }
}
```

Свойства `K0`, `K1`, `K2` удалить целиком. Не оставляй их как `[Obsolete]`-обёртки над `Keys`:
они бессмысленны при 4+ уровнях и только продлят жизнь неверной модели.

**2.** `ClayGroupingEngine.cs` — `BuildGroupAggregateSql` без потолка:

```csharp
    /// <summary>
    /// Строит агрегатный SQL-запрос GROUP BY (SQL Server 2008 R2 совместимый).
    /// Возвращает по одной ключевой колонке K{i} на каждый уровень группировки + COUNT(*) AS Cnt.
    /// Число уровней не ограничено — сколько колонок передано, столько и будет.
    /// </summary>
    /// <param name="selectSql">Базовый SELECT без WHERE/ORDER BY.</param>
    /// <param name="groupExprs">Выходные имена колонок группировки в порядке приоритета. Не пустой.</param>
    /// <param name="where">WHERE-фрагмент или null.</param>
    /// <param name="sortColumns">Текущая сортировка — определяет ORDER BY агрегата.</param>
    /// <exception cref="ArgumentException">groupExprs пуст — вызывающий обязан это проверять.</exception>
    public static string BuildGroupAggregateSql(
        string selectSql,
        IReadOnlyList<string> groupExprs,
        string? where,
        IReadOnlyList<SortColumn> sortColumns)
    {
        if (groupExprs.Count == 0)
            throw new ArgumentException("Список колонок группировки пуст.", nameof(groupExprs));

        var selectParts = groupExprs.Select((expr, i) => $"{expr} AS K{i}");

        var grp = string.Join(", ", groupExprs);
        var ordParts = groupExprs.Select(expr =>
        {
            var sc = sortColumns.FirstOrDefault(s => s.Column == expr);
            return sc is not null ? $"{expr} {(sc.Desc ? "DESC" : "ASC")}" : expr;
        });

        var sql = $"SELECT {string.Join(", ", selectParts)}, COUNT(*) AS Cnt";
        sql += $" FROM ({selectSql}) _g";
        if (where is not null)
            sql += $" WHERE {where}";
        sql += $" GROUP BY {grp}";
        sql += $" ORDER BY {string.Join(", ", ordParts)}";
        return sql;
    }
```

Что изменилось и почему:

- цикл `for (int i = 0; i < 3; i++)` → `Select((expr, i) => ...)` по фактическому списку;
- **добивка `CAST(NULL AS SQL_VARIANT) AS K{i}` исчезла.** Она существовала только чтобы
  Dapper нашёл поля `K1`/`K2` в `GridGroupRow`. Полей больше нет — добивка не нужна.
  Заодно уходит источник путаницы «NULL = уровня нет» (GN0, «Факт 4»);
- `ORDER BY` агрегата **обязан** совпадать с `GROUP BY` по составу и порядку: `BuildTree`
  требует, чтобы родительский агрегат в списке шёл ПЕРЕД дочерним. Логика `ordParts` не
  меняется, но не вздумай её «оптимизировать».

**3.** Создать `Components/Grid/ClayGroupRowMapper.cs` — общий маппер для обоих режимов:

```csharp
namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Маппинг строк агрегатного GROUP BY-запроса (словарь колонка→значение) в
/// <see cref="GridGroupRow"/>. Имена колонок K{i}/Cnt задаёт
/// <see cref="ClayGroupingEngine.BuildGroupAggregateSql"/>.
/// Общий для статического и динамического режимов. ЧИСТЫЙ класс — тестируется без БД.
/// </summary>
public static class ClayGroupRowMapper
{
    private const string CountColumn = "Cnt";

    /// <summary>
    /// Мапит одну строку агрегата.
    /// </summary>
    /// <param name="row">Строка результата (Dapper-словарь или словарь DynamicSql).</param>
    /// <param name="levelCount">
    /// Число уровней группировки. Задаётся вызывающим по числу группировочных колонок,
    /// а НЕ угадывается по наличию ключей в строке: null — законное значение ключа.
    /// </param>
    public static GridGroupRow MapRow(IReadOnlyDictionary<string, object?> row, int levelCount)
    {
        var keys = new List<object?>(levelCount);
        for (int i = 0; i < levelCount; i++)
            keys.Add(Normalize(row.GetValueOrDefault($"K{i}")));

        return new GridGroupRow
        {
            Keys = keys,
            Cnt  = ToInt(Normalize(row.GetValueOrDefault(CountColumn))),
        };
    }

    /// <summary>
    /// Мапит все строки агрегата, СОХРАНЯЯ порядок из БД: BuildTree требует, чтобы
    /// родительский агрегат предшествовал дочернему, и этот порядок задаёт ORDER BY агрегата.
    /// </summary>
    public static List<GridGroupRow> MapRows(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows, int levelCount)
        => rows.Select(r => MapRow(r, levelCount)).ToList();

    /// <summary>DBNull → null. BuildAggregates различает null и значение, DBNull его обманет.</summary>
    private static object? Normalize(object? v) => v is DBNull ? null : v;

    private static int ToInt(object? v) => v switch
    {
        null  => 0,
        int i => i,
        _     => Convert.ToInt32(v),
    };
}
```

**Никаких `OrderBy`/`Distinct`/`GroupBy` в маппере.** Порядок строк значим.

**4.** Если существует `Components/Grid/Dynamic/ClayDynamicGroupMapper.cs` (создан в GG1) —
**удалить его**, а вызовы `ClayDynamicGroupMapper.MapRows(rawRows)` заменить на
`ClayGroupRowMapper.MapRows(rawRows, exprs.Count)`. Места вызова: `ClayGrid.Dynamic.Grouping.cs`
(GG2) и `ClayGrid.Dynamic.Export.cs` (GE3) — **найди все**. Тесты `ClayDynamicGroupMapper`
перенеси на `ClayGroupRowMapper`, добавив параметр `levelCount`.

Если файла нет (GG1 не выполнялся) — пропусти этот пункт и скажи об этом в отчёте.

**5.** `ClayGridPageBase*` — четыре места `Db.QueryAsync<GridGroupRow>(groupSql, dp)`.
Dapper больше не сможет замапить агрегат на `GridGroupRow` (имена колонок переменные, свойство
одно). Переводим на словари:

```csharp
        var groupSql = ClayGroupingEngine.BuildGroupAggregateSql(selectSql, exprs, where, _query.SortColumns);

        // Dapper не мапит переменный набор K{i} на GridGroupRow — читаем словарями (GN1).
        var rawGroups = await Db.QueryAsync(groupSql, dp);
        var groupRows = ClayGroupRowMapper.MapRows(
            rawGroups.Cast<IDictionary<string, object?>>()
                     .Select(d => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(d)),
            exprs.Count);
```

Все четыре места: `ClayGridPageBase.LoadGroupedData`, `ClayGridPageBase.Export.Print.BuildAllGroupedRowsForPrint`,
`ClayGridPageBase.Export.Excel.BuildAllGroupedRowsForExcel`, `ClayGridPageBase.Export.Selected.BuildAllGroupedRowsForSelected`.
**Найди их все — их ровно четыре, `grep` по `QueryAsync<GridGroupRow>`.**

Про приведение: Dapper на нетипизированный `QueryAsync` возвращает `IEnumerable<dynamic>`, где
каждая строка — `DapperRow`, реализующий `IDictionary<string, object>`. Nullable-аннотации в
рантайме стёрты, поэтому `IDictionary<string, object?>` — тот же тип. **Проверь это на реальном
запуске в блоке «Проверка», а не на веру:** если приведение упадёт, сделай явный проход по
парам ключ-значение, но НЕ меняй сигнатуру маппера.

В разных местах переменная с колонками группировки называется по-разному (`exprs`, `groupCols`) —
подставь фактическое имя, не переименовывай.

## Не делай

**Не трогай `BuildAggregates`** — это GN2. После GN1 он читает `Keys[0]` и `Keys[1]` через
временный код (см. ниже) и ведёт себя ровно как раньше. Не трогай `BuildTree`,
`ComputeParentCounts`, `ComputeEffectiveRows`, `WalkTree`, `BuildDetailPageSql`,
`BuildDetailOrder`. Не меняй сборку детального `WHERE` — это GN3. Не вводи лимит на число
уровней. Не оставляй `K0/K1/K2` как obsolete-обёртки.

**Временный мост в `BuildAggregates`.** Чтобы шаг компилировался, замени в нём чтение
`gr.K0`/`gr.K1` на эквивалент поверх `Keys` — с точно тем же поведением, включая дефекты:

```csharp
        foreach (var gr in groupRows)
        {
            var keys = new List<string>();
            // GN1: временный мост — поведение 1:1 как было (2 уровня, null = уровня нет).
            // Настоящая логика на N уровней — GN2. НЕ ЧИНИТЬ ЗДЕСЬ.
            var k0 = gr.Keys.Count > 0 ? gr.Keys[0] : null;
            var k1 = gr.Keys.Count > 1 ? gr.Keys[1] : null;
            if (k0 is not null) keys.Add(k0.ToString()!);
            if (k1 is not null) keys.Add(k1.ToString()!);

            var depth = keys.Count - 1;
            var rawKeyValues = new object?[] { k0, k1 }.Take(keys.Count).ToList();
            /* … остальное тело без изменений … */
        }
```

Это единственная правка в `BuildAggregates` на этом шаге. Комментарий «НЕ ЧИНИТЬ ЗДЕСЬ» оставь —
GN2 его снимет.

## Проверка

**Юнит (`BuildGroupAggregateSql`):**

- одна колонка `["a"]` → `SELECT a AS K0, COUNT(*) AS Cnt FROM (...) _g GROUP BY a ORDER BY a`;
  **`CAST(NULL AS SQL_VARIANT)` в результате НЕТ**;
- две колонки `["a","b"]` → `a AS K0, b AS K1`, `GROUP BY a, b`;
- **пять колонок** `["a","b","c","d","e"]` → `K0..K4`, `GROUP BY a, b, c, d, e`,
  `ORDER BY a, b, c, d, e` — потолка нет;
- `sortColumns = [("b", Desc)]` при `["a","b"]` → `ORDER BY a, b DESC`;
- `where = "x = 1"` → `WHERE x = 1` между `FROM` и `GROUP BY`;
- пустой `groupExprs` → `ArgumentException`.

**Юнит (`ClayGroupRowMapper`):**

- `{"K0": "Кровь", "Cnt": 5}`, `levelCount = 1` → `Keys.Count == 1`, `Keys[0] == "Кровь"`, `Cnt == 5`;
- `{"K0": "a", "K1": "b", "K2": "c", "Cnt": 2}`, `levelCount = 3` → `Keys` = `["a","b","c"]`;
- `levelCount = 2`, а в словаре есть `K2` → `Keys.Count == 2` (лишнее игнорируется);
- `levelCount = 3`, а в словаре только `K0` → `Keys` = `["a", null, null]`, **исключения нет**;
- `{"K1": DBNull.Value}` → `Keys[1] == null` (а не `DBNull`);
- `{"K0": null}`, `levelCount = 1` → `Keys[0] == null`, **не пустая строка** — это GN2 решает,
  что показывать;
- `{"Cnt": (long)7}` → `Cnt == 7`;
- `MapRows` на трёх словарях → три элемента **в том же порядке**.

**Регрессия — главное на этом шаге.** Поведение 1–2 уровней НЕ должно измениться нигде:

Статический режим (`MedicalTests.razor`):

- группировка по одной колонке → как раньше: заголовки, счётчики, раскрытие, пагинация;
- группировка по двум колонкам → двухуровневое дерево как раньше;
- в профайлере агрегатный запрос теперь **без** `CAST(NULL AS SQL_VARIANT) AS K1` — это
  ожидаемо и единственное видимое изменение;
- «Печать → Все данные» и «Excel → Все данные» с группировкой → как раньше (это два из
  четырёх переведённых мест);
- «Печать → Выбранные» и «Excel → Выбранные» с группировкой → как раньше (ещё два);
- **приведение `DapperRow` к словарю не упало** — если упало, чини приведение, а не маппер.

Динамический режим (если сделан GG7): группировка по одной и двум колонкам — как раньше.

**Три уровня по-прежнему дают дубликаты — это ожидаемо на GN1.** Не пытайся починить здесь.
Зафиксируй в отчёте фактическое поведение и переходи к GN2.
