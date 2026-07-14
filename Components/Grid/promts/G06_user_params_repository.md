> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# G6 — параметры пользователя (класс данных в Lib.Entities, INSERT-only)

ВАЖНО (архитектура): SQL выполняется ТОЛЬКО в `Clayzor.Lib.Entities` через `DynamicSql` (G1b).
Controls вызывает этот класс, передавая инжектированный `DbManager Db`.

Прочитать перед началом: `src/Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` (G1b),
`src/Clayzor.Lib.Entities/Entity.cs` (стиль классов данных).

## Файл создать: `src/Clayzor.Lib.Entities/DynamicGrid/ClayGridUserParamsData.cs`
```csharp
namespace Clayzor.Lib.Entities.DynamicGrid;

public static class ClayGridUserParamsData
{
    // Имя параметра = Префикс + gridId. ЧИСТАЯ функция (тестируется без БД).
    public static string BuildParamName(string prefix, int gridId) => prefix + gridId;

    // Чистые строители SQL (тестируются без БД).
    public static string BuildLoadSql(string table, ClayGridSchemaMap s, int nameCount);
    public static string BuildInsertSql(string table, ClayGridSchemaMap s);

    // Читает (Параметр → Значение) для клиента по ТОЧНЫМ именам (prefix+gridId).
    public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(
        DbManager db, int clientId, IReadOnlyList<string> paramNames,
        string table, ClayGridSchemaMap s, CancellationToken ct = default)
    {
        var sql  = BuildLoadSql(table, s, paramNames.Count);   // WHERE [ClientId]=@clid AND [Name] IN (@n0,@n1,...)
        var prm  = new DynamicParameters(); /* clid + n0..nN */
        var rows = await DynamicSql.QueryRowsAsync(db, sql, prm, ct);
        return rows.ToDictionary(r => (string)r[s.UserParams.Name]!, r => (string?)r[s.UserParams.Value] ?? "");
    }

    // Сохранение — ТОЛЬКО INSERT. Триггер БД решает insert/update (см. G0).
    public static Task SaveAsync(
        DbManager db, int clientId, string name, string value,
        string table, ClayGridSchemaMap s, CancellationToken ct = default)
        => DynamicSql.ExecuteAsync(db, BuildInsertSql(table, s), new { clid = clientId, name, value }, ct);
}
```
Форма SQL (имена — из схемы, в квадратных скобках; значения — только параметрами):
- Load: `SELECT [<Name>],[<Value>] FROM [<UserParamsTable>] WHERE [<ClientId>] = @clid AND [<Name>] IN (@n0,@n1,…)`
- Insert: `INSERT INTO [<UserParamsTable>] ([<ClientId>],[<Name>],[<Value>]) VALUES (@clid,@name,@value)`

Не делай: НИКАКОГО `UPDATE`/`MERGE` в C# — только INSERT (upsert делает триггер из G0);
не создавай `DbManager` внутри; не выполняй SQL в Controls; CLID по умолчанию 0 подставляет
вызывающий код (G7), а не этот класс.

Проверка (TG5 + TG-INT):
- `BuildParamName("flt",140)` == `"flt140"`; `BuildParamName("cols",1)` == `"cols1"`;
- `BuildInsertSql` содержит `INSERT INTO` и НЕ содержит `UPDATE`/`MERGE`;
- (интеграционно) `SaveAsync(db,0,"flt140","a")` затем `SaveAsync(db,0,"flt140","b")` →
  `LoadAsync(db,0,["flt140"],…)` вернёт `flt140 → "b"` (одна строка, сработал триггер-upsert).
