> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# G2 — чтение определения и колонок (класс данных в Lib.Entities + чистые мапперы)

ВАЖНО (архитектура): доступ к БД — СТРОГО через `Clayzor.Lib.Entities`. Этот шаг создаёт класс
данных в `Lib.Entities`, который выполняет SQL через помощники `DynamicSql` (G1b). Слой Controls
SQL не выполняет и `DbManager` не создаёт — он только вызывает этот класс, передавая `Db`.

Прочитать перед началом: `src/Clayzor.Lib.Entities/Entity.cs` (как устроены классы данных и
статические методы вроде `GetAllAsync(DbManager db)`), `src/Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs`
(создан в G1b), `src/Clayzor.Lib.Entities/ColumnNames.cs` и `SQLQueries.cs` (где принято держать
имена колонок/SQL).

## Файлы создать — ВСЁ в `src/Clayzor.Lib.Entities/DynamicGrid/`

1. Модели (record'ы) — `ClayGridDefinition.cs`, `ClayColumnDefinition.cs`:
```csharp
namespace Clayzor.Lib.Entities.DynamicGrid;

public sealed record ClayGridDefinition(
    int GridId, string? Title, string? IconUrl, string Sql,
    string? IdColumn, string? IdNameColumn,
    string? EditForm, string? NewForm, string? SqlDelete);

public sealed record ClayColumnDefinition(
    int ColumnId, int GridId, string Column, string? Header, string? UrlKey,
    int? Order, string? Format, int Type);
```

2. Класс данных `ClayGridDefinitionData.cs` — здесь выполняется SQL (через DynamicSql):
```csharp
public static class ClayGridDefinitionData
{
    public static async Task<ClayGridDefinition?> LoadGridAsync(
        DbManager db, int gridId, string settingsTable, ClayGridSchemaMap schema, CancellationToken ct = default)
    {
        var sql  = BuildGridSql(settingsTable, schema);                 // чистая функция, см. п.3
        var rows = await DynamicSql.QueryRowsAsync(db, sql, new { gridId }, ct);
        return rows.Count == 0 ? null : MapDefinition(rows[0], schema); // чистый маппер, см. п.3
    }

    public static async Task<IReadOnlyList<ClayColumnDefinition>> LoadColumnsAsync(
        DbManager db, int gridId, string columnsTable, ClayGridSchemaMap schema, CancellationToken ct = default)
    {
        var sql  = BuildColumnsSql(columnsTable, schema);
        var rows = await DynamicSql.QueryRowsAsync(db, sql, new { gridId }, ct);
        return rows.Select(r => MapColumn(r, schema)).ToList();
    }
}
```
Примечание по типу `ClayGridSchemaMap`: он создан в G1 в `Clayzor.Lib.Web.Controls`. Так как
`Lib.Entities` НЕ ссылается на Controls (цепочка: DALC ← Entities ← Web.Settings ← Controls),
**перенеси `ClayGridSchemaMap` в `Clayzor.Lib.Entities/DynamicGrid/`** и обнови `using` в
`ClayGridDynamicOptions` (Controls ссылается на Entities — это разрешено). Если сомневаешься —
остановись и спроси.

3. Чистые функции (тестируются без БД) — в том же классе или `ClayGridDefinitionMapper.cs`:
```csharp
// SQL строится ТОЛЬКО из имён схемы; параметр — @gridId; имена в квадратных скобках.
public static string BuildGridSql(string settingsTable, ClayGridSchemaMap s);
public static string BuildColumnsSql(string columnsTable, ClayGridSchemaMap s);

public static ClayGridDefinition   MapDefinition(IReadOnlyDictionary<string, object?> row, ClayGridSchemaMap s);
public static ClayColumnDefinition MapColumn(IReadOnlyDictionary<string, object?> row, ClayGridSchemaMap s);
```
Форма SQL:
- `SELECT [<GridId>],[<Title>],[<Icon>],[<Sql>],[<Id>],[<IdName>],[<EditForm>],[<NewForm>],[<SqlDelete>] FROM [<SettingsTable>] WHERE [<GridId>] = @gridId`
- `SELECT [<ColumnId>],[<GridId>],[<Column>],[<Header>],[<UrlKey>],[<Order>],[<Format>],[<Type>] FROM [<ColumnsTable>] WHERE [<GridId>] = @gridId ORDER BY [<Order>]`

Не делай: не создавай `DbManager` внутри — он приходит параметром; не выполняй SQL в слое
Controls; не отбрасывай колонки с `Порядок` NULL/0 (видимость решается позже); не хардкодь
русские имена в SQL — только через схему.

Проверка (TG2 + TG-INT):
- `MapColumn` на строке {КодКолонки:1005, КодЗапроса:140, Колонка:"Активно", ЗаголовокКолонки:"Активно",
  КлючURL:"active", Порядок:0, Формат:"Активно=1", Тип:7} → `Order==0`, `Type==7` (не отброшено);
- `BuildColumnsSql` с изменённой схемой (Header:"Caption") содержит `[Caption]` — имена берутся из схемы;
- (интеграционно) `LoadColumnsAsync(db,140,...)` → 5 колонок в порядке `Порядок` (1005 — последняя).
