> Часть плана «Группировка динамического грида». Перед началом прочитай **GG0_README_dynamic_grouping.md** и **_readme_grid_dynamic.md**. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GG1 — выполнение агрегатного запроса групп из динамического режима

Прочитать перед началом (обязательно, до написания кода):

- `Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` — целиком. Обрати внимание на
  `QueryRowsAsync`: как он превращает результат Dapper в `IReadOnlyDictionary<string, object?>`.
- `Components/Grid/ClayGroupingEngine.cs` — типы `GridGroupRow` (поля `K0`, `K1`, `K2`, `Cnt`)
  и метод `BuildGroupAggregateSql` (какие имена колонок он выдаёт в SELECT).
- `Components/Grid/ClayGridPageBase.cs`, метод `LoadGroupedData` — строка
  `var groupRows = await Db.QueryAsync<GridGroupRow>(groupSql, dp);`. Это то, что нужно
  повторить, но без прямого Dapper.
- `src/Clayzor.Lib.Web.Controls/AGENTS.md` — правило про запрет прямых вызовов Dapper из
  `Controls`.

## Задача

Статический режим получает строки агрегата так:

```csharp
var groupSql  = ClayGroupingEngine.BuildGroupAggregateSql(selectSql, exprs, where, _query.SortColumns);
var groupRows = await Db.QueryAsync<GridGroupRow>(groupSql, dp);
```

`Db.QueryAsync<GridGroupRow>` — это Dapper с автомаппингом на класс. Динамическому режиму так
нельзя: правило архитектуры (см. G4, `AGENTS.md`) — **грид не выполняет SQL сам, он вызывает
классы `Clayzor.Lib.Entities`, передавая им инжектированный `DbManager`**. Плюс `GridGroupRow`
живёт в `Controls`, а `Entities` про него не знает и знать не должен — зависимость идёт в одну
сторону.

Решение: `Entities` отдаёт словари (это он уже умеет — `DynamicSql.QueryRowsAsync`), `Controls`
мапит их в `GridGroupRow` своей чистой функцией. Никаких новых типов в `Entities`.

## Изменить/создать

**1.** Создать `Components/Grid/Dynamic/ClayDynamicGroupMapper.cs`:

```csharp
namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Маппинг строк агрегатного GROUP BY-запроса (словарь колонка→значение из
/// <see cref="Clayzor.Lib.Entities.DynamicGrid.DynamicSql.QueryRowsAsync"/>)
/// в <see cref="GridGroupRow"/> для <see cref="ClayGroupingEngine"/>.
/// Имена колонок K0/K1/K2/Cnt задаёт <see cref="ClayGroupingEngine.BuildGroupAggregateSql"/>.
/// ЧИСТАЯ функция — тестируется без БД.
/// </summary>
public static class ClayDynamicGroupMapper
{
    /// <summary>Имена колонок агрегатного запроса — должны совпадать с BuildGroupAggregateSql.</summary>
    private const string K0 = "K0";
    private const string K1 = "K1";
    private const string K2 = "K2";
    private const string Cnt = "Cnt";

    /// <summary>Мапит одну строку агрегата. DBNull приводится к null.</summary>
    public static GridGroupRow MapRow(IReadOnlyDictionary<string, object?> row) => new()
    {
        K0  = Normalize(row, K0) ?? "",
        K1  = Normalize(row, K1),
        K2  = Normalize(row, K2),
        Cnt = ToInt(Normalize(row, Cnt)),
    };

    /// <summary>Мапит все строки агрегата, сохраняя порядок из БД (он значим для BuildTree).</summary>
    public static List<GridGroupRow> MapRows(IEnumerable<IReadOnlyDictionary<string, object?>> rows)
        => rows.Select(MapRow).ToList();

    private static object? Normalize(IReadOnlyDictionary<string, object?> row, string key)
    {
        var v = row.GetValueOrDefault(key);
        return v is DBNull ? null : v;
    }

    private static int ToInt(object? v) => v switch
    {
        null   => 0,
        int i  => i,
        _      => Convert.ToInt32(v),
    };
}
```

Три вещи, которые легко сделать неправильно:

- **Порядок строк.** `MapRows` обязан сохранять порядок, в котором строки пришли из БД.
  `ClayGroupingEngine.BuildTree` опирается на то, что родительский агрегат в списке идёт
  ПЕРЕД дочерним, а `BuildGroupAggregateSql` ставит `ORDER BY` именно для этого. Никаких
  `OrderBy`/`Distinct`/`GroupBy` в маппере.
- **`K0` не может быть `null`** — тип поля `object` (не `object?`), `BuildAggregates` делает
  `gr.K0.ToString()!`. При `NULL` в группировочной колонке подставляй `""`, а не `null`.
  Это осознанная деградация: `NULL` попадёт в группу с пустым именем.
- **`DBNull`.** `BuildGroupAggregateSql` для незаданных уровней пишет
  `CAST(NULL AS SQL_VARIANT) AS K1`. Dapper обычно отдаёт `null`, но `DBNull` тоже возможен —
  `BuildAggregates` проверяет `gr.K1 is not null`, и `DBNull` эту проверку ПРОЙДЁТ, породив
  фантомный второй уровень группировки. Отсюда `Normalize`.

**2.** `Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` — ничего добавлять НЕ нужно.
`QueryRowsAsync(db, sql, param, ct)` уже подходит: агрегатный запрос не постраничный, параметры
идут в `DynamicParameters`. **Убедись в этом чтением сигнатуры**, и если реально не подходит —
ОСТАНОВИСЬ и спроси, не изобретай новый метод.

## Не делай

Не добавляй `GridGroupRow` и другие типы `Controls` в `Clayzor.Lib.Entities` — зависимость
идёт `Controls → Entities`, не наоборот. Не вызывай `Db.QueryAsync<T>` из `Controls`.
Не меняй `ClayGroupingEngine` — ни `BuildGroupAggregateSql`, ни `BuildAggregates`
(про K2 и два уровня см. GG0, раздел «Ограничения»). Не трогай `LoadDynamicData` — это GG2.
Никакой новой разметки.

## Проверка (юнит-тесты, новый файл в проекте тестов Controls)

БД не нужна — маппер чистый.

- `MapRow` на словаре `{ "K0": "Кровь", "K1": null, "K2": null, "Cnt": 5 }` →
  `K0 == "Кровь"`, `K1 == null`, `K2 == null`, `Cnt == 5`;
- `DBNull.Value` вместо `null` в `K1` → `K1 == null` (а НЕ `DBNull`);
- `"K0": null` → `K0 == ""` (пустая строка, не `null`);
- `"Cnt": (long)7` (SQL Server может отдать не `int`) → `Cnt == 7`;
- ключа `K2` в словаре нет вообще → `K2 == null`, исключения нет;
- `MapRows` на трёх словарях → `List<GridGroupRow>` из трёх элементов В ТОМ ЖЕ ПОРЯДКЕ;
- сквозной: `MapRows(...)` → `ClayGroupingEngine.BuildAggregates(...)` → `BuildTree(...)`
  на двухуровневом наборе (`{Кровь, A, 2}`, `{Кровь, B, 3}`, `{Моча, C, 1}`) даёт 2 корня,
  у первого 2 потомка; после `ComputeParentCounts` у корня «Кровь» `ItemCount == 5`.
- `dotnet build` зелёный, поведение грида не изменилось ни в одном режиме (новый код пока
  никем не вызывается).
