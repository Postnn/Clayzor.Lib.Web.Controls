# CTFR3.2 — Завершить behavioral tests DALC connectivity contract

## Scope
Это test-only corrective task после CTFR3.1. Production CTFR3 принят. Production semantics не менять. Если новые tests выявят production defect — не исправлять его здесь, а описать отдельно.

## 1. Исправить fake IClayTreeMutations injection

Сейчас CTFR3.1 задаёт `TableName = "dbo.T"` и reflection-ом пишет только `_mutationsCached = fake`. После CTFR1 это ненадёжно: getter `Mutations` сверяет `_mutationsKey`, может сбросить fake и при заданном TableName создать настоящий `ClaySqlTreeMutations`.

Использовать реальный DI branch production-кода:

```csharp
TableName = null;
_ctx.Services.AddSingleton<IClayTreeMutations>(mutations);
```

Регистрация должна происходить до render. Не устанавливать `_mutationsCached` или `_mutationsKey` reflection-ом.

Каждый mutation test обязан проверять flag fake (`AddChildCalled`, `DeleteCalled`, `ReorderCalled/ReparentCalled`), чтобы доказать, что production реально использовал fake.

## 2. AddChild failed mutation

Сохранить/исправить `AddChild_SqlException_DoesNotRunSuccessPath`.

Fake `AddChildAsync` бросает ordinary `SqlException`.

Проверить:
- fake реально вызван;
- success-path не меняет `HasChildren`/expanded state;
- `ReloadLevelAsync` не выполняется;
- datasource не получает post-mutation reload.

Если перед mutation сделано `ds.LoadCallCount = 0`, после failed mutation ожидается `0`, а не комментарий/assert про initial LoadRoots.

## 3. Delete failed mutation

Сохранить/исправить `Delete_SqlException_DoesNotRunSuccessPath`.

Создать реальное precondition: node выбран. Fake `DeleteAsync` бросает `SqlException`.

Проверить:
- fake вызван;
- selection после ошибки сохранён;
- reload не выполнен;
- post-mutation datasource calls == 0.

## 4. DnD failed mutation — ОБЯЗАТЕЛЬНО

Добавить behavioral test настоящего ClayTree DnD production path, используя наиболее простой существующий `ReorderAsync` или `ReparentAsync`.

Fake mutation бросает `SqlException`.

Проверить:
- fake DnD method реально вызван;
- success-path не фиксирует изменение дерева;
- reload success-path не выполняется;
- selection/expanded state не повреждены;
- datasource не получает success reload calls.

Не копировать DnD algorithm в test.

## 5. SaveState best-effort

Существующий `SaveState_SqlException_SwallowedAsBestEffort` оставить, но throwing store должен иметь `Called`/`SaveCallCount`.

Проверить одновременно:
```text
SaveAsync реально вызван;
SaveStateAsync не бросает exception.
```

## 6. Zero affected rows — ОБЯЗАТЕЛЬНЫЙ CTFR3 contract

Добавить behavioral test:

`ExecuteAsync_ZeroAffectedRows_ReturnsZeroWithoutError`

Должно быть доказано:
```text
SQL/action успешно выполнена;
affected rows == 0;
ExecuteAsync возвращает 0;
ISqlErrorHandler не вызывается;
exception отсутствует.
```

Ключевой инвариант:
```text
connectivity failure -> exception
successful DML affecting 0 rows -> 0
```

Нельзя моделировать zero rows connectivity exception-ом.

## 7. Ordinary non-connectivity SqlException

Добавить behavioral test:

`ExecuteAsync_NonConnectivitySqlException_Throws`

Создать synthetic ordinary SqlException (например number 547) и sanity-check:

```csharp
Assert.False(DbManager.IsConnectivityError(ex));
```

Проверить:
- exception выходит наружу;
- не превращается в 0;
- handler count соответствует принятому production CTFR3 contract;
- gate освобождается.

Сначала прочитать production `DbManager` и тестировать его фактический контракт, не угадывать handler semantics.

## 8. Cancellation

Добавить behavioral test:

`RunAsync_Cancellation_PropagatesAndReleasesGate`

Проверить:
- `OperationCanceledException`/`TaskCanceledException` propagates;
- не превращается в connectivity fallback;
- handler не вызывается;
- gate освобождается;
- следующая operation может войти в gate.

Предпочтительно cancellation после входа в RunAsync, если существующий seam это позволяет.

## 9. Без обязательного внешнего SQL Server

Не делать test suite зависимым от установленного SQL Server.

Сначала изучить `DbManager` и существующие seams: connection factory/delegate/internal test infrastructure/fake DbConnection.

Закрытый `127.0.0.1:59999` допустим для connectivity smoke tests, но НЕ доказывает ordinary SqlException, zero rows и cancellation внутри action.

Если эти три behavioral cases невозможно сделать без нового production seam:
- НЕ менять production самостоятельно;
- остановиться;
- в отчёте точно указать, какого seam не хватает;
- предложить минимальный production seam отдельным следующим corrective task.

## 10. Read connectivity tests

Оставить:
```text
QueryAsync_Connectivity_ReturnsEmpty
ExecuteScalarAsync_Connectivity_ReturnsDefault
QueryStoredProcAsync_Connectivity_ReturnsEmpty
```

Каждый должен проверять и fallback result, и:
```csharp
Assert.Equal(1, handler.CallCount);
```

## 11. DynamicSql handler-once

Усилить:
```text
DynamicSql_QueryRowsAsync_Connectivity_ReturnsEmpty
DynamicSql_QueryCountAsync_Connectivity_ReturnsZero
DynamicSql_ExecuteAsync_Connectivity_Throws
```

Для каждого добавить:
```csharp
Assert.Equal(1, handler.CallCount);
```

Это доказывает отсутствие double handling между DynamicSql и DbManager.

## 12. Gate release

Существующий connectivity gate-release test оставить.

Если seam позволяет, добавить gate-release после non-connectivity SqlException и cancellation.

## 13. SqlExceptionFactory

Reflection factory допустим в test assembly. Он должен позволять создавать ordinary non-connectivity SqlException.

Перед behavioral test обязательно:
```csharp
Assert.False(DbManager.IsConnectivityError(ex));
```

Если создаётся synthetic connectivity exception — также sanity-check `Assert.True(...)`.

## 14. Production changes запрещены

Не менять:
- DbManager semantics;
- connectivity classification;
- read fallback/write throw;
- DynamicSql production code;
- ClayTree mutation catches;
- ClayTree paging/CTFR2.x;
- CTFR1 mutation cache;
- datasource contracts.

Если новый test красный из-за production defect, отчёт:
```text
FAILED CONTRACT
test:
expected:
actual:
production location:
suggested next corrective task:
```

## 15. Минимальный набор tests

```text
AddChild_SqlException_DoesNotRunSuccessPath
Delete_SqlException_DoesNotRunSuccessPath
DragDrop_SqlException_DoesNotRunSuccessPath
SaveState_SqlException_SwallowedAsBestEffort

RunAsync_Connectivity_HandlerCalledOnceAndThrows
RunAsync_Connectivity_GateReleasedAfterException
ExecuteAsync_Connectivity_ThrowsNotZero
ExecuteAsync_Connectivity_HandlerCalledExactlyOnce

ExecuteAsync_ZeroAffectedRows_ReturnsZeroWithoutError
ExecuteAsync_NonConnectivitySqlException_Throws
RunAsync_Cancellation_PropagatesAndReleasesGate

QueryAsync_Connectivity_ReturnsEmpty
ExecuteScalarAsync_Connectivity_ReturnsDefault
QueryStoredProcAsync_Connectivity_ReturnsEmpty

DynamicSql_QueryRowsAsync_Connectivity_ReturnsEmpty_HandlerOnce
DynamicSql_QueryCountAsync_Connectivity_ReturnsZero_HandlerOnce
DynamicSql_ExecuteAsync_Connectivity_Throws_HandlerOnce
```

Имена можно адаптировать к реальному API.

## 16. Self-check

A. Остались tests, которые inject fake только через `_mutationsCached`? Если да — исправить.

B. Есть настоящий behavioral zero-affected-rows test? Если нет — задача не завершена.

C. Есть ordinary non-connectivity SqlException behavioral test? Если нет — не завершена.

D. Есть cancellation test с handler/gate assertions? Если нет — не завершена.

E. Все representative DynamicSql tests проверяют handler count? Если нет — исправить.

F. Есть настоящий production DnD mutation-abort test? Если нет — исправить.

G. SaveState test доказывает, что throwing store реально вызван? Если нет — исправить.

## 17. Build/tests

Запустить полный relevant solution build и полный test suite, не только новые classes. Указать точные passed/failed/skipped.

## 18. AGENTS.md/docs

Следовать существующим правилам проекта. Это test-only task; production contract docs не менять без реальной необходимости.

## Acceptance report

Указать:
1. изменённые файлы;
2. `production code: 0 semantic changes`;
3. как fake mutations теперь проходят через production DI path;
4. AddChild result;
5. Delete result;
6. DnD result;
7. SaveState call proof;
8. connectivity write/read results;
9. zero rows result;
10. ordinary SqlException result;
11. cancellation result;
12. gate-release results;
13. DynamicSql handler-once results;
14. build result;
15. полный test suite result.

Если zero rows / ordinary SqlException / cancellation требуют нового production seam, не скрывать ограничение и не менять production самостоятельно. Описать минимальный необходимый seam для отдельного решения человеком.
