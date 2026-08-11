# CTFR3.5 — Вынести connectivity handler за пределы `_gate`

## ВАЖНО

Это узкий corrective task после CTFR3.4.

CTFR3.4 уже принят по следующим частям:

- public API `DbManager.Connection` снова `SqlConnection`;
- public `RunAsync<T>` снова принимает `Func<SqlConnection, Task<T>>`;
- internal `DbConnection` seam сохранён;
- zero affected rows test есть;
- ordinary non-connectivity `SqlException` test есть;
- cancellation test есть;
- same-instance gate-release tests есть.

Исправить ОДНУ проблему:

> `ISqlErrorHandler.HandleSqlError(...)` для connectivity сейчас вызывается внутри `RunCoreAsync` до выхода из `finally`, то есть пока `_gate` ещё захвачен.

Это нарушает существующий DALC-инвариант.

Не менять остальные semantics.

---

# 1. Текущий проблемный код

Сейчас структура примерно такая:

```csharp
private async Task<T> RunCoreAsync<T>(
    Func<DbConnection, Task<T>> action)
{
    await _gate.WaitAsync();

    try
    {
        return await action(GetDbConnection());
    }
    catch (SqlException ex) when (IsConnectivityError(ex))
    {
        _errorHandler?.HandleSqlError(
            ex,
            _connectionString,
            "RunAsync",
            []);

        throw;
    }
    finally
    {
        _gate.Release();
    }
}
```

Порядок:

```text
gate acquire
operation throws connectivity SqlException
handler executes
gate release
exception propagates
```

Это неверно.

---

# 2. Требуемый инвариант

Connectivity handler должен выполняться ТОЛЬКО после освобождения `_gate`.

Правильный порядок:

```text
gate acquire
operation
gate release
handler
throw
```

Причина:

`ISqlErrorHandler` не должен выполняться под DALC semaphore.

Это уже зафиксировано в `AGENTS.md`.

Не удалять этот архитектурный инвариант из документации.

---

# 3. Предпочтительная структура

Не обязательно копировать буквально, но решение должно разделить:

```text
execution under gate
```

и:

```text
error handling outside gate
```

Например:

```csharp
private async Task<T> RunCoreAsync<T>(
    Func<DbConnection, Task<T>> action)
{
    try
    {
        return await RunUnderGateAsync(action);
    }
    catch (SqlException ex) when (IsConnectivityError(ex))
    {
        _errorHandler?.HandleSqlError(
            ex,
            _connectionString,
            "RunAsync",
            []);

        throw;
    }
}
```

где:

```csharp
private async Task<T> RunUnderGateAsync<T>(
    Func<DbConnection, Task<T>> action)
{
    await _gate.WaitAsync();

    try
    {
        return await action(GetDbConnection());
    }
    finally
    {
        _gate.Release();
    }
}
```

Это только пример.

Можно использовать другую минимальную структуру.

Но должно быть доказуемо:

```text
к моменту HandleSqlError semaphore уже released
```

---

# 4. НЕ дублировать gate logic

Не создавать несколько независимых копий:

```text
RunAsync gate logic
RunDbAsync gate logic
ExecuteAsync gate logic
```

Должен остаться единый execution core.

Public:

```csharp
RunAsync<T>(Func<SqlConnection, Task<T>>)
```

и internal:

```csharp
RunDbAsync<T>(Func<DbConnection, Task<T>>)
```

должны проходить через один и тот же gate implementation.

---

# 5. Не менять handler count semantics

После исправления:

### connectivity

```text
handler вызывается ровно 1 раз
exception пробрасывается из RunAsync
```

### ordinary non-connectivity SqlException

Не менять текущий CTFR3 contract:

```text
RunCore не вызывает connectivity handler
wrapper вызывает handler один раз
exception пробрасывается
```

### cancellation

```text
handler не вызывается
OperationCanceledException propagates
```

---

# 6. Не менять read/write behavior

Оставить:

```text
ExecuteAsync:
    connectivity -> throw
    non-connectivity SqlException -> throw
    successful 0 affected rows -> return 0

QueryAsync / QueryStoredProcAsync:
    connectivity -> empty
    ordinary SqlException -> throw

ExecuteScalarAsync:
    connectivity -> default
    ordinary SqlException -> throw
```

Не менять эти paths.

---

# 7. Не менять public API

После CTFR3.5 обязаны остаться:

```csharp
public SqlConnection Connection
```

и:

```csharp
public Task<T> RunAsync<T>(
    Func<SqlConnection, Task<T>> action)
```

Не возвращать CTFR3.3 breaking API.

---

# 8. Internal DbConnection seam сохранить

Оставить testability:

```csharp
internal DbManager(
    string connectionString,
    ISqlErrorHandler? errorHandler,
    Func<DbConnection> connectionFactory)
```

и internal execution seam, если он сейчас есть:

```csharp
RunDbAsync<T>(...)
```

или эквивалент.

CTFR3.3/3.4 behavioral tests должны продолжать работать без реального SQL Server.

---

# 9. Главный regression test — handler вызывается после gate release

Добавить test, который реально доказывает порядок.

Не ограничиваться проверкой:

```text
после exception следующий ExecuteAsync работает
```

Это уже есть и доказывает release после завершения operation, но НЕ доказывает, что handler не был вызван раньше release.

Нужен handler, который во время `HandleSqlError` пытается использовать ТОТ ЖЕ `DbManager`.

---

# 10. Reentrant handler test

Сделать test-specific handler, который получает ссылку на тот же `DbManager`.

Например концептуально:

```csharp
private sealed class ReentrantHandler : ISqlErrorHandler
{
    public DbManager Db { get; set; } = null!;
    public bool ReentrantOperationSucceeded { get; private set; }

    public void HandleSqlError(...)
    {
        var result = Db.RunDbAsync<int>(
            _ => Task.FromResult(123))
            .GetAwaiter()
            .GetResult();

        ReentrantOperationSucceeded = result == 123;
    }
}
```

Это только пример.

Важно избежать deadlock самого test framework.

Если synchronous `GetAwaiter().GetResult()` внутри handler опасен, сделать другой deterministic probe.

Главный смысл:

```text
handler должен иметь возможность войти в тот же _gate
```

Если handler вызван под gate — test зависнет/упадёт по timeout.

Если gate released — operation succeeds.

---

# 11. Предпочтительный безопасный test без вечного hang

Не создавать тест, который может зависнуть навсегда.

Использовать timeout.

Например handler запускает:

```csharp
Db.RunDbAsync<int>(
    _ => Task.FromResult(123))
```

и ждёт максимум короткий deterministic timeout.

Или test handler проверяет gate через controlled probe, если test assembly имеет internal access.

Допустимый вариант:

```csharp
var task = Db.RunDbAsync<int>(
    _ => Task.FromResult(123));

Assert.True(
    task.Wait(TimeSpan.FromSeconds(1)));
```

или async-safe эквивалент.

Не использовать длинные таймауты.

---

# 12. Ещё лучше — explicit gate probe, если доступно

Если возможно без публичного API добавить internal test-only seam:

```csharp
internal Task<T> RunDbAsync<T>(...)
```

уже достаточно.

Не добавлять `IsGateFree` public property.

Не добавлять public semaphore exposure.

---

# 13. Test scenario

Нужен controlled connectivity `SqlException`.

Использовать synthetic `SqlExceptionFactory`.

Сначала sanity check:

```csharp
Assert.True(
    DbManager.IsConnectivityError(ex));
```

Если существующий factory создаёт только ordinary error, создать connectivity code, например:

```text
53
```

или другой код из `ConnectivityErrorCodes`.

---

# 14. Controlled fake connection

Использовать existing CTFR3.3/3.4:

```text
FakeConnection : DbConnection
FakeCommand : DbCommand
```

Первый command:

```text
throws connectivity SqlException
```

Handler внутри ошибки пытается выполнить controlled operation на том же DbManager.

Для reentrant operation connection/command infrastructure должна иметь следующий controlled success result.

Например command queue:

```text
1 -> throw connectivity SqlException
2 -> return 123
```

---

# 15. Expected result

Test должен доказать:

```text
первый ExecuteAsync throws connectivity SqlException;
handler called exactly once;
handler's same-DbManager reentrant operation succeeds;
no deadlock;
gate remains usable afterward.
```

Например:

```csharp
await Assert.ThrowsAsync<SqlException>(
    () => db.ExecuteAsync(...));

Assert.Equal(1, handler.CallCount);
Assert.True(handler.ReentrantOperationSucceeded);
```

После handler можно дополнительно выполнить ещё одну operation на том же db, если command queue позволяет.

---

# 16. Внимание: handler не должен сам генерировать новый SqlException

Reentrant handler operation должна быть успешной.

Иначе test может уйти в recursive error handling.

Использовать deterministic success action.

---

# 17. Ordinary SqlException regression оставить

Существующий:

```text
ExecuteAsync_NonConnectivitySqlException_ThrowsAndReleasesGate
```

должен остаться зелёным.

Не менять handler placement для ordinary SQL wrapper logic без необходимости.

---

# 18. Cancellation regression оставить

Существующий:

```text
RunAsync_Cancellation_PropagatesAndReleasesGate
```

должен остаться зелёным.

---

# 19. Zero rows regression оставить

Существующий:

```text
ExecuteAsync_ZeroAffectedRows_ReturnsZeroWithoutError
```

должен остаться зелёным.

---

# 20. Existing real connectivity tests оставить

Bad-port tests:

```text
RunAsync_Connectivity_HandlerCalledOnceAndThrows
ExecuteAsync_Connectivity_ThrowsNotZero
QueryAsync_Connectivity_ReturnsEmpty
...
```

должны остаться зелёными.

Это подтверждает, что handler relocation не сломал настоящий `SqlConnection` path.

---

# 21. AGENTS.md

Сохранить и при необходимости уточнить инвариант:

```text
ISqlErrorHandler вызывается после освобождения `_gate`.
Handler не выполняется внутри DALC semaphore.
```

Не писать только абстрактное «handler once».

Указать именно ordering.

---

# 22. Не менять

Не менять:

- `Connection: SqlConnection`;
- public `RunAsync<Func<SqlConnection,...>>`;
- internal `DbConnection` test seam;
- connectivity error codes;
- `IsConnectivityError`;
- read fallback;
- write throw;
- zero rows;
- DynamicSql;
- ClayTree;
- CTFR1;
- CTFR2.x;
- unrelated DALC code.

---

# 23. Self-check

### A

В месте вызова:

```csharp
HandleSqlError(...)
```

`_gate` уже released?

Если невозможно доказать по структуре кода — задача не выполнена.

### B

Есть один общий gate implementation?

Если gate logic скопирована в два метода — исправить.

### C

Reentrant handler test использует тот же DbManager?

Если нет — test не доказывает ordering.

### D

Handler reentrant operation реально проходит через тот же `_gate`?

Если нет — test слабый.

### E

Test имеет timeout/защиту от вечного зависания?

Если нет — добавить.

### F

Все CTFR3.3/3.4 tests зелёные?

Если нет — regression.

---

# 24. Build / tests

Запустить:

```text
полный build;
полный test suite.
```

Указать:

```text
passed:
failed:
skipped:
```

---

# Приёмка

В финальном отчёте обязательно указать:

1. точную новую структуру gate/error handling;
2. где теперь происходит `_gate.Release()`;
3. где теперь вызывается connectivity `HandleSqlError`;
4. доказательство, что handler вызывается после release;
5. reentrant same-DbManager handler test;
6. timeout protection test;
7. handler count;
8. zero rows regression;
9. ordinary SqlException regression;
10. cancellation regression;
11. bad-port connectivity regression;
12. public API signatures unchanged;
13. production files changed;
14. test files changed;
15. build result;
16. full test suite result.

Главный acceptance criterion:

```text
connectivity handler выполняется только после освобождения `_gate`,
при этом CTFR3 error semantics и public API не меняются.
```
