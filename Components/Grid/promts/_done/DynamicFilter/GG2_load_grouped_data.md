> Часть плана «Группировка динамического грида». Перед началом прочитай **GG0_README_dynamic_grouping.md** и **_readme_grid_dynamic.md**. Требует выполненного **GG1**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GG2 — `LoadDynamicGroupedData`: конвейер группировки

Прочитать перед началом (обязательно, до написания кода):

- **`Components/Grid/ClayGridPageBase.cs`, метод `LoadGroupedData` (самый конец файла) —
  это эталон. Прочитай его построчно.** Динамический вариант отличается ровно двумя вещами:
  откуда берутся строки и во что заворачиваются.
- `Components/Grid/ClayGroupingEngine.cs` — целиком.
- `Components/Grid/ClayGrid.Dynamic.cs` — `LoadDynamicData` (после GF16 там уже есть
  `dp.Add("search", ...)`), `SelectSql`, `DefaultOrder`, `SearchColumns`, `_dynamicKnownColumns`,
  `SaveDynamicState`.
- `Components/Grid/Dynamic/ClayDynamicRow.cs` (GF1) и `ClayDynamicGroupMapper.cs` (GG1).
- `Components/Grid/ClayDataQuery.cs` — `GroupEnabled`, `GroupColumns`, `ExpandedGroups`,
  `BuildOrderBy`, `BuildWhereClause`, `CombineWhere`.
- `Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` — `QueryRowsAsync`.

## Задача

`LoadDynamicData` сейчас всегда строит плоский список: `query.GroupEnabled` и
`query.GroupColumns` не читаются вообще. Нужен второй путь загрузки — групповой, и диспетчер
между ними, ровно как `ClayGridPageBase.LoadData` разводит `LoadGroupedData` / `LoadFlatData`.

**На этом шаге фича пользователю не видна**: `Groupable = false` (GF14), кнопки «Группировать»
нет, `_groupColumns` всегда пуст, диспетчер всегда уходит в плоскую ветку. Так и должно быть —
включение в GG7. Проверка этого шага — юнит-тесты и временный хак, описанный ниже.

## Изменить/создать

**1.** Создать `Components/Grid/ClayGrid.Dynamic.Grouping.cs` — новый partial того же класса:

```csharp
using Clayzor.Lib.Entities.DynamicGrid;
using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Dapper;

namespace Clayzor.Lib.Web.Controls.Components.Grid;

/// <summary>
/// Группировка в динамическом режиме ClayGrid.
/// Переиспользует <see cref="ClayGroupingEngine"/> целиком; отличие от статического режима
/// (<c>ClayGridPageBase.LoadGroupedData</c>) — строки берутся через <see cref="DynamicSql"/>
/// и заворачиваются в <see cref="ClayDynamicRow"/> вместо <c>DetailRow&lt;T&gt;</c>.
/// </summary>
public partial class ClayGrid<TEntity> where TEntity : class
{
    /// <summary>Корни дерева групп последней загрузки. null — плоский режим или данных нет.</summary>
    private List<GridGroupNode>? _dynamicGroupRoots;

    /// <summary>Кеш: глубина → FullKey всех групп на ней. Сбрасывается при каждой загрузке.</summary>
    private Dictionary<int, List<string>>? _dynamicGroupKeysByDepth;

    private async Task LoadDynamicGroupedData(ClayDataQuery query, string? where, DynamicParameters dp)
    {
        var exprs = query.GroupColumns.ToList();

        // ── 1. Агрегат: одна строка на листовую группу ──────────────────────────
        var groupSql  = ClayGroupingEngine.BuildGroupAggregateSql(SelectSql, exprs, where, query.SortColumns);
        var rawRows   = await DynamicSql.QueryRowsAsync(Db, groupSql, dp);
        var groupRows = ClayDynamicGroupMapper.MapRows(rawRows);

        // ── 2. Дерево групп ────────────────────────────────────────────────────
        var aggregates = ClayGroupingEngine.BuildAggregates(groupRows);
        var roots      = ClayGroupingEngine.BuildTree(aggregates);
        ClayGroupingEngine.ComputeParentCounts(roots);

        _dynamicGroupRoots       = roots;
        _dynamicGroupKeysByDepth = null;   // кеш глубин пересоберётся лениво (GG5)

        // ── 3. Разметка текущей страницы ───────────────────────────────────────
        int totalEffective = roots.Sum(r => ClayGroupingEngine.ComputeEffectiveRows(r, query.ExpandedGroups));
        int pageStart      = (query.PageNumber - 1) * query.PageSize + 1;
        int pageEnd        = query.PageNumber * query.PageSize;
        var layout         = new List<GridLayoutItem>();
        int cur            = 1;
        ClayGroupingEngine.WalkTree(roots, query.ExpandedGroups, pageStart, pageEnd, ref cur, layout);

        // ── 4. Строки: заголовки групп + детали раскрытых групп ────────────────
        var orderBy     = query.BuildOrderBy(DefaultOrder);
        var detailOrder = ClayGroupingEngine.BuildDetailOrder(orderBy, query.GroupColumns, DefaultOrder);
        var newRows     = new List<TEntity>();

        foreach (var item in layout)
        {
            if (item.Header is not null)
                newRows.Add((TEntity)(object)item.Header);

            if (!item.HasDetailRange || item.Aggregate is null) continue;

            var ag           = item.Aggregate;
            var detailParams = new DynamicParameters();
            detailParams.AddDynamicParams(dp);

            var keyParts = ag.RawKeys
                .Select((k, i) => { detailParams.Add($"dk{i}", k); return $"{exprs[i]} = @dk{i}"; })
                .ToList();
            var detailWhere = ClayDataQuery.CombineWhere(where, string.Join(" AND ", keyParts));

            detailParams.Add("__start", item.DetailStart);
            detailParams.Add("__end",   item.DetailEnd);

            var sql  = ClayGroupingEngine.BuildDetailPageSql(SelectSql, detailWhere, detailOrder);
            var rows = await DynamicSql.QueryRowsAsync(Db, sql, detailParams);
            newRows.AddRange(rows.Select(r => (TEntity)(object)new ClayDynamicRow(r)));
        }

        Items      = newRows;
        TotalCount = totalEffective;
    }
}
```

Разбор мест, где легко ошибиться:

- **`(TEntity)(object)`** — и для `GroupHeaderRow`, и для `ClayDynamicRow`. `TEntity` в
  `Home.razor` = `IClayGridRow`, оба типа его реализуют, но компилятор про `TEntity` этого не
  знает. Собирай сразу `List<TEntity>`, а не `List<IClayGridRow>` с последующим кастом всего
  списка — тот каст упадёт `InvalidCastException` ровно как в GF1.
- **`TotalCount = totalEffective`**, а не число записей в БД. В режиме группировки «строка» —
  это заголовок группы ИЛИ строка детализации раскрытой группы. Пагинатор считает страницы от
  этого числа. Это не опечатка и не баг — так же делает статический режим.
- **`detailParams.AddDynamicParams(dp)`** — параметры базового `where` (`@search` и параметры
  фильтра) нужны и в детальном запросе. Новый `DynamicParameters` на КАЖДУЮ группу: `dk0`/`dk1`
  и `__start`/`__end` у каждой группы свои. Не переиспользуй `dp`.
- **`ag.RawKeys`** — исходные (нетипизированные) значения ключей, не строки из `KeyValues`.
  Передаются параметрами (`@dk0`), а вот `exprs[i]` (имя колонки) подставляется в текст SQL —
  поэтому список группировочных колонок обязан быть провалидирован (см. GG0, «Модель доверия»,
  и GG7).
- **`RawKeys` пуст у синтетических родительских узлов** (`BuildAggregates` кладёт им `RawKeys = []`).
  Но `HasDetailRange` у них не выставляется — `WalkTree` ставит его только листовым узлам
  (`node.Children.Count == 0`). Проверка `if (!item.HasDetailRange || item.Aggregate is null) continue;`
  этого достаточно.
- **`BuildDetailOrder`** нужен, потому что `query.BuildOrderBy` уже включает группировочные
  колонки в начало (см. `ClayDataQuery.BuildOrderBy`, ветка `GroupEnabled`), а внутри группы
  сортировать по ним бессмысленно — там одно значение.

**2.** `ClayGrid.Dynamic.cs`, `LoadDynamicData` — разделить на подготовку `where`/`dp` и
диспетчер:

```csharp
private async Task LoadDynamicData(ClayDataQuery query)
{
    var dp = new DynamicParameters();
    dp.Add("search", $"%{query.SearchText}%");          // GF16

    var searchWhere = query.BuildWhereClause(SearchColumns);
    var filterWhere = ClayCompositeSqlBuilder.Build(query.CompositeFilter, dp, _dynamicKnownColumns);
    var where       = ClayDataQuery.CombineWhere(searchWhere, filterWhere);

    if (query.GroupEnabled && query.GroupColumns.Count > 0)
        await LoadDynamicGroupedData(query, where, dp);
    else
        await LoadDynamicFlatData(query, where, dp);

    // Сохраняем состояние после каждой загрузки данных
    await SaveDynamicState();
}

/// <summary>Плоский режим: страница строк без группировки.</summary>
private async Task LoadDynamicFlatData(ClayDataQuery query, string? where, DynamicParameters dp)
{
    _dynamicGroupRoots       = null;
    _dynamicGroupKeysByDepth = null;

    var orderBy = query.BuildOrderBy(DefaultOrder);

    var rows = await DynamicSql.QueryPagedRowsAsync(
        Db, SelectSql, where, orderBy, dp, query.PageNumber, query.PageSize);

    TotalCount = await DynamicSql.QueryCountAsync(Db, SelectSql, where, dp);
    Items      = rows.Select(r => (TEntity)(object)new ClayDynamicRow(r)).ToList();
}
```

Сброс `_dynamicGroupRoots`/`_dynamicGroupKeysByDepth` в плоской ветке обязателен — иначе
дерево от прошлой группировки останется жить и GG5 покажет чипы уровней для несуществующих
групп. Так же делает `ClayGridPageBase.LoadFlatData`.

`SaveDynamicState()` вынесен из веток в общий хвост — вызывался и раньше после каждой загрузки,
поведение не меняется.

## Не делай

Не меняй `ClayGroupingEngine` — ни одной строки. Не меняй `ClayDataQuery`. Не включай
группировку (`Groupable` остаётся `false`) — это GG7. Не трогай `OnGroupToggle`, `_expandedGroups`,
`GroupRowHostKey`, чипы уровней — это GG3/GG4/GG5. Не добавляй справочники в `DisplayValue`
заголовка группы — это GG6. Не вызывай Dapper напрямую (`DynamicParameters` — это модель
параметров, её использовать можно; `Db.QueryAsync` — нельзя).

## Проверка

**Юнит (без БД):** сквозной тест конвейера без обращения к БД невозможен — `LoadDynamicGroupedData`
ходит в базу. Проверяй составляющие:

- `ClayGroupingEngine.BuildGroupAggregateSql("SELECT a,b FROM T", ["a"], null, [])` содержит
  `a AS K0`, `CAST(NULL AS SQL_VARIANT) AS K1`, `GROUP BY a`;
- `BuildDetailOrder("КодТипа ASC, Название ASC", ["КодТипа"], "КодИсследования")` →
  `"Название ASC"`; `BuildDetailOrder("КодТипа ASC", ["КодТипа"], "КодИсследования")` →
  `"КодИсследования"` (fallback).

**Ручная (временный хак, откатить после проверки):** в `InitDynamicMode` временно допиши в самый
конец, перед `await NotifyQueryChanged();`:

```csharp
_groupColumns.Add("КодТипа");   // ВРЕМЕННО — убрать после проверки GG2
```

Затем `?id=140&CLID=9`:

- в SQL-профайлере видно агрегатный запрос с `GROUP BY КодТипа` и `COUNT(*) AS Cnt`;
- в гриде — строки заголовков групп (шеврон + значение + «(N шт.)»), все свёрнуты;
- «Всего: N записей», где N = количество РАЗЛИЧНЫХ значений `КодТипа` (все группы свёрнуты →
  эффективных строк ровно столько, сколько групп);
- детальных строк нет, детальных запросов в профайлере нет (ничего не раскрыто);
- шеврон кликается, но ничего не происходит — это ожидаемо, обработчик в GG3;
- колонка `Тип исследования` скрылась из грида (`Hidden="@IsGrouped(sqlName)"`) — ожидаемо;
- ввести текст в поиск → агрегатный запрос ушёл с `WHERE ... LIKE @search`, ошибки
  «Must declare the scalar variable @search» НЕТ (GF16);
- **убрать временную строку**, пересобрать, `?id=140` → грид снова плоский, регрессии нет.

Статический режим (`MedicalTests.razor`): группировка работает как раньше — `LoadGroupedData`
не тронут.
