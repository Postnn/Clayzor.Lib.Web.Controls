> Часть плана «Динамический режим ClayGrid». Перед началом прочитай **readme_grid_dynamic.md** (разделы «Как работать» и «Общие правила»). Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# G1b — помощники динамического SQL в Lib.Entities (весь доступ к БД — здесь)

Зачем: по архитектуре доступ к БД идёт СТРОГО через `Clayzor.Lib.Entities`. Для динамического
грида нужны запросы с ПРОИЗВОЛЬНОЙ формой строки (колонки заранее не известны), поэтому
добавляем универсальные помощники в Lib.Entities. Слой Controls дальше вызывает только их.

Прочитать перед началом: `src/Clayzor.Lib.Entities/Entity.cs` (существующие
`GetPagedAsync<T>`/`GetCountAsync<T>`/`GetAllAsync<T>(DbManager db, string selectSql, …)`,
`InsertAsync/UpdateAsync/DeleteAsync(db)`); `src/Clayzor.Lib.DALC/DbManager.cs`; как проект
использует Dapper. НЕ дублируй логику — переиспользуй существующие помощники, где возможно.

Файлы создать: `src/Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` (static-класс).
Методы (все принимают инжектируемый `DbManager db`, никакого создания DbManager внутри
Controls; SQL — параметризован через `@param`):
```csharp
namespace Clayzor.Lib.Entities.DynamicGrid;

public static class DynamicSql
{
    // Произвольный SELECT → строки как словари колонка→значение (Dapper dynamic).
    public static Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryRowsAsync(
        DbManager db, string sql, object? param = null, CancellationToken ct = default);

    // Постраничный произвольный SELECT (SQL Server 2008 R2-совместимый — как в Entity.GetPagedAsync).
    // Вернуть страницу строк-словарей; общее количество — QueryCountAsync.
    public static Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryPagedRowsAsync(
        DbManager db, string selectSql, string? where, string? orderBy,
        object? param, int pageNumber, int pageSize, CancellationToken ct = default);

    public static Task<int> QueryCountAsync(
        DbManager db, string selectSql, string? where, object? param = null, CancellationToken ct = default);

    // Пары (значение, текст) для справочников Тип 5 (первая колонка → вторая).
    public static Task<IReadOnlyList<(object? Value, string? Text)>> QueryPairsAsync(
        DbManager db, string sql, object? param = null, CancellationToken ct = default);

    // Тройки (значение, tooltip, iconHref) для Тип 9.
    public static Task<IReadOnlyList<(object? Value, string? Text, string? Icon)>> QueryTriplesAsync(
        DbManager db, string sql, object? param = null, CancellationToken ct = default);

    // Не-запрос (DELETE/INSERT по произвольному SQL, напр. SqlDelete). Вернуть кол-во строк.
    public static Task<int> ExecuteAsync(
        DbManager db, string sql, object? param = null, CancellationToken ct = default);
}
```
Пагинацию (`QueryPagedRowsAsync`/`QueryCountAsync`) реализуй тем же способом, что уже
применяется в `Entity.GetPagedAsync`/`GetCountAsync` (ROW_NUMBER(), 2008 R2), чтобы поведение
совпадало со статическим гридом.

Опционально (если динрежиму нужна строка подключения ПО ИМЕНИ, отличная от дефолтной):
добавь в Lib.Entities провайдер `IDbManagerFactory.Create(string connectionStringName)` и
его реализацию (резолвинг строки — через существующий механизм Web.Settings). Создание
`DbManager` — ТОЛЬКО здесь, не в Controls.

Не делай: не выполняй SQL и не создавай `DbManager` в слое Controls; не вводи Dapper-зависимость
в Controls; не пиши свой пейджинг, отличный от существующего в Entity.

Проверка:
- сборка зелёная; `DynamicSql` доступен из Controls как единственная точка выполнения
  произвольного SQL;
- интеграционно (TG-INT): `QueryRowsAsync(db, "SELECT 1 AS a, 'x' AS b")` → одна строка
  {a:1, b:"x"}; `QueryPairsAsync(db, "SELECT КодТипа, Наименование FROM Типы")` → список пар;
  `ExecuteAsync(db, "DELETE FROM T WHERE Id=@id", new{ id=1 })` → число затронутых строк.
