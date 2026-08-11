# CTFR3.3 — Testability seam для DbManager через injectable connection factory

## ВАЖНО

Это узкий production-testability task.

Цель НЕ менять runtime semantics `DbManager`, а добавить минимальный seam, который позволит behavioral-test три недостающих контракта CTFR3:

```text
ExecuteAsync_ZeroAffectedRows_ReturnsZeroWithoutError
ExecuteAsync_NonConnectivitySqlException_Throws
RunAsync_Cancellation_PropagatesAndReleasesGate
```

Причина: сейчас `DbManager` сам создаёт `SqlConnection` и выполняет `Connection.Open()` до пользовательского action. Без реального SQL Server невозможно добраться до контролируемого action-path для этих сценариев.

Предлагаемый seam: injectable `Func<SqlConnection>` / connection factory.

Не расширять задачу за пределы этого.

---

# 1. Сначала изучить текущий DbManager

Перед правкой открыть и изучить:

- `DbManager` constructor(s);
- `RunAsync`;
- `ExecuteAsync`;
- `QueryAsync`;
- `ExecuteScalarAsync`;
- `QueryStoredProcAsync`;
- disposal/lifetime;
- semaphore/gate;
- `ConnectionString`;
- `ISqlErrorHandler`;
- CTFR3/CTFR3.1/CTFR3.2 tests.

Нужно понять точное место создания `SqlConnection`.

Не менять exception flow без необходимости.

---

# 2. Требуемый seam

Добавить в `DbManager` минимальный способ подменить создание подключения.

Предпочтительно private/internal field:

```csharp
private readonly Func<SqlConnection> _connectionFactory;
```

Default constructor behavior:

```csharp
_connectionFactory = () => new SqlConnection(connectionString);
```

Все production callers без нового аргумента должны работать ровно как сейчас.

Допустимые API варианты:

### Вариант A — optional internal constructor

Например:

```csharp
public DbManager(string connectionString, ISqlErrorHandler errorHandler)
    : this(connectionString, errorHandler, () => new SqlConnection(connectionString))
{
}

internal DbManager(
    string connectionString,
    ISqlErrorHandler errorHandler,
    Func<SqlConnection> connectionFactory)
{
    ...
}
```

Это предпочтительно, если test assembly уже имеет `InternalsVisibleTo`.

### Вариант B — optional parameter

Допустимо:

```csharp
public DbManager(
    string connectionString,
    ISqlErrorHandler errorHandler,
    Func<SqlConnection>? connectionFactory = null)
```

и:

```csharp
_connectionFactory = connectionFactory ?? (() => new SqlConnection(connectionString));
```

Но сначала оцени API impact.

Предпочесть минимальный breaking surface.

---

# 3. НЕ менять default runtime semantics

Для обычного production caller:

```csharp
new DbManager(connectionString, handler)
```

поведение должно остаться:

```text
создаётся настоящий SqlConnection с тем же connectionString;
Open/OpenAsync вызывается там же;
Dapper используется как раньше;
handler/exception semantics без изменений.
```

Не вводить fake behavior в production path.

---

# 4. Connection factory contract

Factory должен создавать НОВЫЙ connection object на каждую operation, если именно так работает текущий DbManager.

Не кэшировать один `SqlConnection` между operations, если сейчас connection создаётся каждый раз.

DbManager по-прежнему отвечает за корректный dispose connection.

Например:

```csharp
await using var connection = _connectionFactory();
```

или эквивалент текущего lifecycle.

Не менять ownership semantics.

---

# 5. Тип factory

Предпочтительно оставить:

```csharp
Func<SqlConnection>
```

если весь DALC жёстко завязан на `Microsoft.Data.SqlClient.SqlConnection`.

НЕ переходить в этой задаче на `DbConnection`, новый abstraction layer, interface hierarchy или repository pattern.

CTFR3.3 — минимальный seam, не DALC rewrite.

---

# 6. Test fake connection

После seam создать test-only fake/derived `SqlConnection` либо другой минимальный controllable connection.

Сначала проверить, какие члены `SqlConnection` virtual и что реально можно подменить.

Если `SqlConnection` нельзя удобно наследовать/подменить для Dapper command execution, допускается другая минимальная test seam стратегия.

НО:

Не менять production API дальше необходимого без отдельного обоснования.

Если окажется, что `Func<SqlConnection>` недостаточно, остановиться и описать почему.

Не превращать CTFR3.3 автоматически в большой abstraction over ADO.NET.

---

# 7. Zero affected rows test

Обязательный behavioral test:

```text
ExecuteAsync_ZeroAffectedRows_ReturnsZeroWithoutError
```

Нужно реально пройти production `DbManager.ExecuteAsync`.

Fake connection/command должен моделировать:

```text
Open succeeds
ExecuteNonQueryAsync returns 0
```

Проверить:

```csharp
var result = await db.ExecuteAsync(...);

Assert.Equal(0, result);
Assert.Equal(0, handler.CallCount);
```

Exception отсутствует.

Критически важно: `0` должен прийти из успешного command execution, а НЕ из exception/default branch.

---

# 8. Ordinary non-connectivity SqlException test

Обязательный behavioral test:

```text
ExecuteAsync_NonConnectivitySqlException_Throws
```

Fake connection open succeeds.

Во время command execution бросить synthetic ordinary `SqlException`, например number 547.

Перед test:

```csharp
Assert.False(DbManager.IsConnectivityError(ex));
```

Проверить фактический CTFR3 contract:

```text
exception выходит наружу;
не превращается в 0/default;
handler вызывается ровно ожидаемое число раз;
gate освобождается.
```

Если production contract после CTFR3 говорит handler once:

```csharp
Assert.Equal(1, handler.CallCount);
```

После exception выполнить вторую operation и доказать, что gate не остался захвачен.

---

# 9. Cancellation test

Обязательный behavioral test:

```text
RunAsync_Cancellation_PropagatesAndReleasesGate
```

Open должен успешно пройти.

Action должен бросить:

```csharp
OperationCanceledException
```

или:

```csharp
TaskCanceledException
```

Проверить:

```text
cancellation выходит наружу;
ISqlErrorHandler не вызывается;
exception не превращается в SQL/default fallback;
gate освобождается.
```

После cancellation выполнить следующую controlled operation.

---

# 10. Если RunAsync сам всегда открывает connection

Использовать factory так, чтобы test connection успешно считался open.

Не обходить `RunAsync` reflection-ом.

Behavioral test должен идти через настоящий production `RunAsync`.

---

# 11. Existing connectivity smoke tests оставить

Текущие tests через:

```text
127.0.0.1:59999
```

оставить.

Они полезны как integration-like smoke test настоящего SqlConnection connectivity classification.

Новый seam НЕ должен заменить их fake-only tests.

Итого должны существовать два слоя:

```text
real SqlConnection bad-port connectivity smoke tests
+
controlled factory behavioral branch tests
```

---

# 12. DynamicSql не менять

CTFR3.2 уже закрыл DynamicSql handler-once.

CTFR3.3 не должен менять DynamicSql production code.

Новые tests DynamicSql не обязательны, если seam нужен только для DbManager contract.

---

# 13. ClayTree не менять

CTFR3.2 уже закрыл:
- DI mutation fake;
- AddChild abort;
- Delete abort;
- DnD abort;
- SaveState best-effort.

CTFR3.3 не менять Tree production/tests без необходимости.

---

# 14. Handler counting fake

Использовать существующий `CountingHandler`.

Для three new tests:

### zero rows

```text
CallCount == 0
```

### ordinary SqlException

```text
CallCount == production contract expected value
```

### cancellation

```text
CallCount == 0
```

---

# 15. Gate release

Проверить gate после:

```text
ordinary SqlException
cancellation
```

Не ограничиваться connectivity branch.

Например:

```text
operation1 fails/cancels
operation2 returns known result
```

Если operation2 завершается — gate release доказан.

---

# 16. Thread safety / factory

Не делать connection factory mutable public property типа:

```csharp
db.ConnectionFactory = ...
```

если можно избежать.

Предпочтительнее constructor injection и readonly field.

Это исключит runtime race при смене factory между operations.

---

# 17. ConnectionString property

Даже при injected factory:

```csharp
DbManager.ConnectionString
```

должен оставаться тем же строковым значением, переданным в constructor.

Handler должен по-прежнему получать ожидаемый connection string.

Не вычислять ConnectionString из fake connection.

---

# 18. Disposal

Добавить test, если просто:

```text
factory-created connection disposed after operation
```

можно легко доказать.

Это полезно, но не обязательно, если текущий code path очевиден и существующие tests покрывают disposal.

Не раздувать задачу.

---

# 19. No semantic production changes

Запрещено менять:

- connectivity code list;
- `IsConnectivityError`;
- handler invocation policy;
- read fallback semantics;
- write throw semantics;
- zero-row semantics;
- cancellation semantics;
- semaphore behavior;
- DynamicSql;
- ClayTree.

Из production допустим только testability seam создания `SqlConnection`.

---

# 20. Если Func<SqlConnection> недостаточно

Это важный стоп-критерий.

Если после изучения выяснится, что из-за Dapper/невиртуальных членов нельзя controlled-test command behavior через fake `SqlConnection`:

НЕ вводить сразу:
- `IDbConnectionFactory`;
- `IDbCommandExecutor`;
- custom Dapper wrapper;
- fake SQL protocol;
- local SQL Server dependency.

Остановиться и написать:

```text
Func<SqlConnection> insufficient because:
...
minimal next seam proposal:
...
estimated production surface:
...
```

Решение о следующем abstraction принимает человек.

---

# 21. Tests naming

Минимально:

```text
ExecuteAsync_ZeroAffectedRows_ReturnsZeroWithoutError
ExecuteAsync_NonConnectivitySqlException_ThrowsAndReleasesGate
RunAsync_Cancellation_PropagatesAndReleasesGate
```

Дополнительно можно:

```text
ConnectionFactory_DefaultConstructor_UsesRealSqlConnection
ConnectionFactory_CreatesNewConnectionPerOperation
```

если это недорого.

---

# 22. Self-check

Перед коммитом:

### A

Обычный production constructor без factory существует и работает как раньше?

Если НЕТ — задача не выполнена.

### B

Новый seam меняет только создание connection?

Если НЕТ — задача разрослась.

### C

Zero rows test реально проходит production `ExecuteAsync` и получает `0` из успешного command execution?

Если НЕТ — задача не выполнена.

### D

Ordinary SqlException test sanity-check:

```csharp
Assert.False(DbManager.IsConnectivityError(ex))
```

есть?

Если НЕТ — добавить.

### E

Cancellation test handler count == 0 и gate release доказан?

Если НЕТ — добавить.

### F

Старые bad-port connectivity tests остаются зелёными?

Если НЕТ — seam сломал runtime semantics.

---

# 23. Build / tests

Запустить:

```text
полный build;
полный test suite.
```

Указать exact passed/failed/skipped.

Особенно подтвердить:

```text
CTFR2.x tests;
CTFR3.1;
CTFR3.2;
CTFR3.3;
```

все зелёные.

---

# 24. AGENTS.md / docs

Обновить DALC `AGENTS.md`, если он есть, коротко:

> DbManager connection creation инжектируется только через constructor seam для testability; default production path использует `new SqlConnection(ConnectionString)` и runtime semantics не меняет.

Не документировать test-specific детали в user-facing docs.

---

# Приёмка

Финальный отчёт должен содержать:

1. точный production seam;
2. почему выбран именно `Func<SqlConnection>`;
3. подтверждение backward compatibility default constructor;
4. ownership/disposal connection;
5. zero rows behavioral result;
6. ordinary non-connectivity SqlException result;
7. ordinary exception handler count;
8. ordinary exception gate-release proof;
9. cancellation result;
10. cancellation handler count;
11. cancellation gate-release proof;
12. старые real bad-port connectivity tests result;
13. production files changed;
14. test files changed;
15. полный build result;
16. полный test suite result.

Если `Func<SqlConnection>` недостаточен — production не расширять дальше без отдельного решения. Описать минимальный следующий seam.
