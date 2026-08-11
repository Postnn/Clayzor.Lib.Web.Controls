# CTFR3.1 — Regression tests нового DALC connectivity-контракта

## Контекст

CTFR3 уже изменил production behavior.

Текущий контракт:

### `DbManager.RunAsync`
- connectivity `SqlException`: `ISqlErrorHandler` вызывается один раз, exception пробрасывается наружу.

### Write API (`ExecuteAsync`)
- connectivity `SqlException` пробрасывается;
- ordinary `SqlException` пробрасывается;
- handler вызывается один раз;
- успешный SQL с `0 affected rows` остаётся валидным success result и НЕ считается connectivity failure.

### Read API
`QueryAsync`, `QueryStoredProcAsync`, `ExecuteScalarAsync` и адаптированные `DynamicSql.Query*`:
- connectivity error после handler превращается в пустой/default read result;
- ordinary SQL error пробрасывается;
- handler не должен вызываться дважды.

### Cancellation
`OperationCanceledException` не должна перехватываться или превращаться в SQL/default-result.

### ClayTree
`catch (SqlException)` в mutation flows должен прерывать success-path:
- без reload;
- без `RefreshNodeTextAsync`;
- без `RestoreFocus`;
- без optimistic state после failed DML.

`SaveStateAsync` является best-effort persistence.

После CTFR3 были добавлены тесты только классификации connectivity error codes. Этого недостаточно.

## Цель

Добавить полноценные behavioral regression tests CTFR3.

Основной production-код НЕ менять, если тесты не выявят реальный дефект.

Если тест обнаруживает defect:
1. сначала зафиксировать failing test;
2. сделать минимальное production исправление;
3. явно указать это в отчёте.

## Репозитории

Проверить актуальную структуру solution и разместить tests там, где они архитектурно принадлежат.

Вероятно затрагиваются:
- `Postnn/Clayzor.Lib.DALC`
- `Postnn/Clayzor.Lib.Entities`
- `Postnn/Clayzor.Lib.Web.Controls`
- `Postnn/Clayzor.Lib.Web.Controls.Tests`

Не складывать все tests в Web.Controls.Tests только по инерции, если есть более подходящий test project.

## Важное требование

Не делать tests, которые только вызывают:

```csharp
DbManager.IsConnectivityErrorCode(...)
```

Это уже покрыто.

Нужны tests реального exception flow.

## Как тестировать SqlException

Изучи текущие test helpers и возможность конструирования `SqlException`.

Предпочтительно:
- существующий factory/helper;
- reflection-based test factory, локализованный в tests;
- либо минимальный internal seam, если без него невозможно.

Не добавлять production API только ради tests без необходимости.

Не требовать реального падения SQL Server/network.

## DALC tests

### Test A — connectivity `RunAsync`

Смоделировать connectivity `SqlException` внутри action.

Проверить:
- наружу выходит `SqlException`;
- `ISqlErrorHandler.HandleSqlError` вызван ровно 1 раз;
- handler получил connectivity exception;
- semaphore/gate после exception освобождён: следующий `RunAsync` выполняется.

### Test B — connectivity `ExecuteAsync` write

Проверить:
- `ExecuteAsync` бросает `SqlException`;
- handler вызван ровно 1 раз;
- не возвращается `0` как failure marker.

### Test C — ordinary SQL `ExecuteAsync`

Проверить:
- ordinary SQL error бросается;
- handler ровно 1 раз.

### Test D — успешный `ExecuteAsync` с `0 affected rows`

Обязательный regression test.

Проверить:
- exception нет;
- handler не вызван;
- `0` возвращается как обычный результат.

Нельзя мокать этот test так, чтобы `0` получался из exception branch.

### Test E — connectivity read

Для representative set:
- `QueryAsync`
- `ExecuteScalarAsync`
- `QueryStoredProcAsync`

Проверить:
- connectivity → empty/default;
- handler ровно 1 раз;
- exception наружу не выходит.

### Test F — ordinary SQL read

Проверить:
- ordinary SQL error бросается;
- handler один раз.

### Test G — cancellation

Для `RunAsync` и хотя бы одного wrapper:
- action бросает `OperationCanceledException`/`TaskCanceledException`;
- exception выходит наружу;
- `ISqlErrorHandler` не вызывается;
- результат не превращается в empty/default.

## DynamicSql tests

Добавить representative tests:

### QueryRowsAsync connectivity
- возвращает `[]`;
- handler повторно не вызывается.

### QueryCountAsync connectivity
- возвращает `0`;
- это read fallback, а не write failure marker.

### DynamicSql ExecuteAsync connectivity
Если write method существует:
- exception пробрасывается;
- fake success не возвращается.

## ClayTree tests

Нужен минимум один behavioral test на mutation success-path abort.

Использовать fake `IClayTreeMutations`, который бросает `SqlException`.

### AddChild
Проверить после failed mutation:
- `parent.HasChildren` не становится optimistic `true`, если был false;
- parent не раскрывается искусственно;
- reload не выполняется / datasource load count не растёт.

### Delete
Проверить:
- selection не изменяется до успешного SQL;
- reload не выполняется.

### Reparent/Reorder
Минимум один DnD test:
- `SqlException` → reload не выполняется;
- `RestoreFocus` success-path не выполняется.

Не обязательно дублировать все mutation methods одинаково, но минимум:
- один menu mutation;
- один DnD mutation.

## SaveStateAsync

Добавить test best-effort semantics, если архитектура позволяет:
- `StateStore.SaveAsync` бросает `SqlException`;
- UI selection/expand action не падает;
- `InvalidOperationException` НЕ подавляется.

## Handler mock

Сделать простой counting fake:
- `CallCount`
- `LastException`
- `LastCommand`
- `LastParameters`

Проверять строго `CallCount == 1`.

## Не менять production semantics

Запрещено ради прохождения tests:
- снова возвращать default из `RunAsync` для write;
- считать `0 affected rows` ошибкой;
- добавлять blanket `catch(Exception)`;
- ловить cancellation;
- вызывать handler второй раз на wrapper-level connectivity;
- менять optional read fallback на throw без отдельного решения.

## Test naming

Примеры:

```text
ExecuteAsync_ConnectivityError_HandlesOnceAndThrows
ExecuteAsync_ZeroAffectedRows_ReturnsZeroWithoutError
QueryAsync_ConnectivityError_HandlesOnceAndReturnsEmpty
RunAsync_Cancellation_PropagatesWithoutHandler
AddChildAsync_MutationSqlException_DoesNotRunSuccessPath
```

## Документация

Если tests подтверждают контракт — production docs не менять, кроме возможного списка tests.

Если найдено расхождение code/docs — исправить минимально и явно отметить.

## Приёмка

В финальном отчёте указать:

1. сколько behavioral tests добавлено;
2. какие DALC paths покрыты;
3. как создаётся test `SqlException`;
4. доказательство handler ровно один раз;
5. test на `0 affected rows`;
6. cancellation tests;
7. DynamicSql coverage;
8. ClayTree success-path abort coverage;
9. SaveState best-effort coverage;
10. результаты полного test suite/build.

Отдельно перечислить любые production changes.
Если production code не менялся — написать это явно.
