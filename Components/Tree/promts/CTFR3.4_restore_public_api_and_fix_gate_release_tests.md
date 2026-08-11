# CTFR3.4 — Сохранить public API DbManager и исправить gate-release tests

## ВАЖНО

Это узкий corrective task после CTFR3.3.

CTFR3.3 доказал, что `Func<DbConnection>` позволяет behavioral-test:

```text
zero affected rows;
ordinary non-connectivity SqlException;
cancellation.
```

Но текущая реализация CTFR3.3 имеет два дефекта:

1. ради test seam изменены существующие **public API contracts** `DbManager`;
2. gate-release tests выполняют вторую операцию на новом `DbManager`, поэтому не доказывают освобождение `_gate` исходного экземпляра.

Исправить ТОЛЬКО эти проблемы.

Не менять CTFR3 error semantics.

---

# 1. Текущий breaking change

До CTFR3.3 public API был:

```csharp
public SqlConnection Connection { get; }

public Task<T> RunAsync<T>(
    Func<SqlConnection, Task<T>> action)
```

CTFR3.3 изменил его на:

```csharp
public DbConnection Connection { get; }

public Task<T> RunAsync<T>(
    Func<DbConnection, Task<T>> action)
```

Это source-breaking изменение для external consumers.

Факт отсутствия usages внутри текущих repositories НЕ делает public API change non-breaking.

## Требование

После CTFR3.4 существующий public API должен снова компилироваться:

```csharp
SqlConnection connection = db.Connection;

await db.RunAsync<int>(
    async (SqlConnection connection) =>
    {
        ...
    });
```

Не требовать от consumer перехода на `DbConnection`.

---

# 2. Сначала проверить usages

Перед реализацией найти все usages:

```text
DbManager.Connection
RunAsync<
.RunAsync(
```

во всех доступных Clayzor repositories.

В отчёте перечислить найденные production usages.

Но независимо от количества usages восстановить прежний public contract, если он был public до CTFR3.3.

---

# 3. Не отказываться от DbConnection testability seam

CTFR3.3 behavioral tests полезны.

Нужно сохранить возможность test-only execution через:

```text
FakeConnection : DbConnection
FakeCommand : DbCommand
```

НЕ возвращаться к `Func<SqlConnection>` как единственному seam — он уже доказан недостаточным.

Нужно разделить:

```text
public compatibility surface
```

и:

```text
internal testable execution core
```

---

# 4. Предпочтительная архитектура

Оставить production default connection настоящим `SqlConnection`.

Внутри `DbManager` допустимо иметь:

```csharp
private readonly Func<DbConnection>? _connectionFactory;
private DbConnection? _connection;
```

или эквивалент.

Public constructor:

```csharp
public DbManager(
    string connectionString,
    ISqlErrorHandler? errorHandler = null)
```

должен использовать production `SqlConnection`.

Internal test constructor может принимать:

```csharp
internal DbManager(
    string connectionString,
    ISqlErrorHandler? errorHandler,
    Func<DbConnection> connectionFactory)
```

Но public API не должен выставлять `DbConnection`.

---

# 5. Public Connection

Восстановить:

```csharp
public SqlConnection Connection
```

для обычного production API.

При default production constructor это должен быть настоящий cached `SqlConnection`, как до CTFR3.3.

Не делать опасный cast:

```csharp
(SqlConnection)_connection
```

в test path, где `_connection` может быть `FakeConnection`.

Test path вообще не должен обращаться к public `Connection`, если injected factory возвращает arbitrary `DbConnection`.

Если текущая структура не позволяет безопасно совместить эти paths, разделить внутренние accessors.

Например концептуально:

```csharp
public SqlConnection Connection
    => GetProductionSqlConnection();

private DbConnection GetConnection()
    => ...;
```

Но не копировать connection lifecycle дважды без необходимости.

Выбрать минимальную реализацию.

---

# 6. Public RunAsync

Восстановить прежний public overload:

```csharp
public Task<T> RunAsync<T>(
    Func<SqlConnection, Task<T>> action)
```

Он должен сохранить прежний production contract.

Для controlled tests нужен internal execution path на `DbConnection`.

Предпочтительно:

```csharp
internal Task<T> RunDbAsync<T>(
    Func<DbConnection, Task<T>> action)
```

или private/internal core:

```csharp
private async Task<T> RunCoreAsync<T>(
    Func<DbConnection, Task<T>> action)
```

Public wrapper:

```csharp
public Task<T> RunAsync<T>(
    Func<SqlConnection, Task<T>> action)
{
    ...
}
```

Internal DALC wrappers (`ExecuteAsync`, `QueryAsync`, etc.) могут использовать testable `DbConnection` core.

Ключевой инвариант:

```text
external public RunAsync remains SqlConnection-based;
internal execution can remain DbConnection-based.
```

---

# 7. НЕ создавать ambiguous overloads

Не оставлять одновременно public:

```csharp
RunAsync<T>(Func<SqlConnection, Task<T>>)
RunAsync<T>(Func<DbConnection, Task<T>>)
```

если обычная lambda:

```csharp
db.RunAsync(async c => ...)
```

становится ambiguous.

`DbConnection` variant должен быть private/internal и желательно иметь другое имя:

```text
RunCoreAsync
RunDbAsync
```

---

# 8. ExecuteAsync и остальные wrappers

`ExecuteAsync`, `ExecuteScalarAsync`, `QueryAsync`, `QueryStoredProcAsync` должны продолжать использовать тот execution core, который поддерживает injected `FakeConnection : DbConnection`.

То есть CTFR3.3 tests:

```text
ExecuteAsync_ZeroAffectedRows_ReturnsZeroWithoutError
ExecuteAsync_NonConnectivitySqlException_Throws...
```

не должны потерять testability после восстановления public API.

Не переводить wrappers обратно на public `Connection` так, чтобы fake seam перестал работать.

---

# 9. Default production semantics

Для:

```csharp
new DbManager(connectionString, handler)
```

должно остаться:

```text
реальный Microsoft.Data.SqlClient.SqlConnection;
та же строка подключения;
lazy open;
то же reuse connection within scoped DbManager;
тот же Dispose;
тот же SemaphoreSlim gate;
тот же handler contract.
```

Никаких новых runtime branches для обычного production caller кроме минимального forwarding/core abstraction.

---

# 10. Исправить ordinary SqlException gate-release test

Текущий CTFR3.3 test ошибочно делает примерно:

```csharp
var db = CreateDb(...throwing...);

await Assert.ThrowsAsync<SqlException>(
    () => db.ExecuteAsync(...));

var db2 = CreateDb(...success...);
await db2.ExecuteAsync(...);
```

Это НЕ проверяет `_gate` первого `db`.

## Требование

Вторая operation должна выполняться на ТОМ ЖЕ:

```csharp
db
```

---

# 11. Как сделать same-instance sequential behavior

Учесть, что `DbManager` может кэшировать один connection instance.

Не предполагать автоматически, что factory будет вызвана повторно после первой operation.

Предпочтительно сделать fake command с последовательным поведением.

Например:

```csharp
var command = new FakeCommand(
    firstException: sqlException,
    subsequentAffectedRows: 5);
```

или очередь:

```csharp
Queue<Func<Task<int>>> executions;
```

Поведение:

```text
1-й ExecuteNonQueryAsync -> throw SqlException 547
2-й ExecuteNonQueryAsync -> return 5
```

Обе операции:

```csharp
await db.ExecuteAsync(...)
```

на одном `DbManager`.

Test:

```csharp
await Assert.ThrowsAsync<SqlException>(
    () => db.ExecuteAsync("DELETE ..."));

var result = await db.ExecuteAsync("DELETE ...");

Assert.Equal(5, result);
```

Если вторая операция завершается, gate release доказан.

---

# 12. Handler count ordinary SqlException

После первой ошибки:

```csharp
Assert.Equal(1, handler.CallCount);
```

После успешной второй операции:

```csharp
Assert.Equal(1, handler.CallCount);
```

Handler не должен вызываться повторно.

Sanity assertion оставить:

```csharp
Assert.False(DbManager.IsConnectivityError(ex));
```

---

# 13. Исправить cancellation gate-release test

Текущий test также создаёт `db2`.

Это не доказывает release gate.

Нужно:

```csharp
var db = ...
```

затем на том же `db`:

```csharp
await Assert.ThrowsAsync<OperationCanceledException>(
    () => ...);
```

после чего:

```csharp
var result = await ... // тот же db
```

и operation должна успешно завершиться.

---

# 14. Cancellation должна быть assertion, не try/catch

Не писать:

```csharp
try
{
    await ...
}
catch (OperationCanceledException)
{
}
```

Так test пройдёт даже если exception вообще не возникнет.

Использовать:

```csharp
await Assert.ThrowsAsync<OperationCanceledException>(...);
```

или подходящий `ThrowsAnyAsync<OperationCanceledException>` если production может выдавать subtype.

После этого:

```csharp
Assert.Equal(0, handler.CallCount);
```

---

# 15. Как тестировать cancellation после восстановления public RunAsync

Если public `RunAsync` снова принимает `SqlConnection`, `FakeConnection : DbConnection` нельзя передать в public delegate.

Это нормально.

Cancellation gate behavior можно проверять через **internal testable execution core**, если именно он содержит тот же `_gate` и тот же try/finally.

Например:

```csharp
await Assert.ThrowsAsync<OperationCanceledException>(
    () => db.RunDbAsync<int>(
        _ => throw new OperationCanceledException()));
```

затем на том же `db`:

```csharp
var result = await db.ExecuteAsync(...);
```

Но test должен доказывать именно production gate implementation, а не отдельную копию gate algorithm.

Идеально public `RunAsync` и internal wrappers должны делегировать в ОДИН общий core с одним `_gate`.

---

# 16. Не дублировать RunAsync algorithm

НЕЛЬЗЯ сделать две независимые реализации:

```text
RunAsync(SqlConnection) -> свой WaitAsync/try/finally
RunDbAsync(DbConnection) -> другой WaitAsync/try/finally
```

Это создаст риск расхождения.

Должен быть один общий gate/error core.

Например концептуально:

```csharp
private async Task<T> RunCoreAsync<T>(
    Func<DbConnection, Task<T>> action)
{
    await _gate.WaitAsync();
    try
    {
        ...
    }
    finally
    {
        _gate.Release();
    }
}
```

А public API — compatibility adapter.

Но сохранить существующий CTFR3 handler behavior.

---

# 17. Очень внимательно с SqlException handler

Не переносить `catch(SqlException)` так, чтобы handler начал выполняться под `_gate`, если до этого CTFR3 специально вынес handler наружу.

Сохранить архитектурный инвариант:

```text
ISqlErrorHandler не выполняется под SemaphoreSlim gate.
```

Если для public compatibility wrapper требуется adapter, не ломать этот порядок.

---

# 18. Zero affected rows regression

Существующий test:

```text
ExecuteAsync_ZeroAffectedRows_ReturnsZeroWithoutError
```

должен остаться зелёным без смысловых изменений:

```text
result == 0
handler.CallCount == 0
```

Это главный write-contract CTFR3.

---

# 19. Ordinary SqlException regression

После исправления test должен доказать одновременно:

```text
547 non-connectivity;
exception propagated;
handler exactly once;
same DbManager performs next successful operation;
gate released.
```

Не использовать новый `DbManager`.

---

# 20. Cancellation regression

Test должен доказать:

```text
OperationCanceledException реально thrown;
handler == 0;
same DbManager выполняет следующую operation;
gate released.
```

Не использовать новый `DbManager`.

---

# 21. Public API compatibility tests

Добавить compile/runtime regression tests, чтобы CTFR3.3 breaking change не вернулся.

Минимально:

```csharp
[Fact]
public void Connection_PublicContract_IsSqlConnection()
{
    var property = typeof(DbManager).GetProperty(nameof(DbManager.Connection));

    Assert.Equal(typeof(SqlConnection), property!.PropertyType);
}
```

И для RunAsync желательно reflection assertion, что существует public generic method с parameter conceptually:

```text
Func<SqlConnection, Task<T>>
```

Либо compile-time helper:

```csharp
static Task<int> UseLegacyRunAsync(DbManager db)
    => db.RunAsync<int>((SqlConnection c) => Task.FromResult(1));
```

Сам факт компиляции test project защищает signature.

Не нужно реально открывать SQL connection для compile-time helper.

---

# 22. Internal seam visibility

Internal `Func<DbConnection>` constructor / `RunDbAsync` допустимы только для test assembly через существующий `InternalsVisibleTo`.

Не делать их public ради tests.

---

# 23. Connection lifecycle

Проверить, что injected fake и production connection:

```text
создаются лениво;
кэшируются/reused так же, как до CTFR3.3;
Dispose DbManager -> Dispose connection;
```

Если CTFR3.3 уже сохранил lifecycle, не переписывать его.

---

# 24. AGENTS.md

Исправить формулировку CTFR3.3, которая сейчас утверждает:

```text
RunAsync<T> принимает Func<DbConnection, Task<T>>
```

как public contract.

После CTFR3.4 документация должна разделять:

```text
public compatibility API -> SqlConnection
internal testability core -> DbConnection
```

Также не писать `0 breaking API changes`, пока это не подтверждено фактическими signatures.

---

# 25. Не менять

Не менять:

- `IsConnectivityError`;
- connectivity numbers;
- CTFR3 handler policy;
- read fallback;
- write throw;
- cancellation semantics;
- DynamicSql;
- ClayTree;
- CTFR1;
- CTFR2.x;
- SQL;
- public constructors, кроме восстановления compatibility;
- unrelated DALC API.

---

# 26. Build / full tests

Запустить полный build всех затронутых проектов.

Запустить полный test suite.

В отчёте указать:

```text
passed:
failed:
skipped:
```

Не только CTFR3.4 tests.

---

# 27. Self-check

### A — public Connection

После изменений:

```csharp
typeof(DbManager)
    .GetProperty("Connection")!
    .PropertyType == typeof(SqlConnection)
```

?

Если НЕТ — задача не выполнена.

### B — public RunAsync

Компилируется:

```csharp
db.RunAsync<int>(
    (SqlConnection c) => Task.FromResult(1));
```

?

Если НЕТ — задача не выполнена.

### C — fake seam

Работает ли:

```text
FakeConnection : DbConnection
```

для `ExecuteAsync` tests?

Если НЕТ — testability CTFR3.3 потеряна.

### D — same gate ordinary exception

Первая и вторая operation используют один и тот же `DbManager` instance?

Если НЕТ — test недействителен.

### E — same gate cancellation

Cancellation и следующая successful operation используют один и тот же `DbManager`?

Если НЕТ — test недействителен.

### F — cancellation assertion

Используется `Assert.Throws...`, а не пустой try/catch?

Если НЕТ — исправить.

### G — handler outside gate

Не изменился ли CTFR3 invariant handler-outside-gate?

Если изменился — задача не принимается.

---

# Приёмка

В финальном отчёте обязательно указать:

1. какие public signatures были восстановлены;
2. все найденные usages `Connection`/`RunAsync`;
3. точную структуру internal `DbConnection` seam;
4. почему public API больше не breaking;
5. подтверждение default `SqlConnection` production path;
6. подтверждение единого gate/error execution core;
7. zero rows test result;
8. ordinary `SqlException` same-instance gate-release result;
9. ordinary handler count;
10. cancellation `Assert.Throws` result;
11. cancellation same-instance gate-release result;
12. cancellation handler count;
13. public API compatibility regression tests;
14. старые bad-port connectivity tests;
15. production files changed;
16. test files changed;
17. build result;
18. полный test suite result.

Главный acceptance criterion:

```text
CTFR3.3 testability сохраняется,
но существующий public SqlConnection-based DbManager API восстановлен,
а gate-release доказан на том же DbManager instance.
```
