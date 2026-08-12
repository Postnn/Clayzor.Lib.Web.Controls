# CGFR2 — Dynamic ClayGrid: exception contract shared settings

## ВАЖНО

Это отдельный corrective task после закрытия CGFR1.x. CGFR1.x уже закрыт. Не трогать lifecycle/reinit/query-cache архитектуру без нового доказанного дефекта.

CGFR2 исправляет ТОЛЬКО exception contract вокруг загрузки shared settings в Dynamic ClayGrid.

Текущее проблемное место:

```text
Components/Grid/ClayGrid.Dynamic.cs
LoadAndValidateSharedParamsAsync(...)
```

Сейчас там фактически:

```csharp
try
{
    sharedParams = await ClayGridSharedParamsData.LoadSharedParamsAsync(
        Db, sharedId, opt.UserParamsShared, paramNames);
}
catch
{
    _dynamicError =
        $"Не удалось загрузить общие настройки №{sharedId}. " +
        "Ссылка недействительна или база данных недоступна.";
    return null;
}
```

Это неправильно: голый `catch` маскирует cancellation, ожидаемую DB/connectivity ошибку и programming bugs одним UI-сообщением.

---

# 1. Обязательный exception contract

Должно быть строго:

```text
OperationCanceledException
    -> propagate
    -> не выставлять _dynamicError
    -> не возвращать null

Expected DB/connectivity exception
    -> допускается преобразовать в user-facing _dynamicError
    -> вернуть null

Unexpected/programming exception
    -> propagate
    -> не выставлять _dynamicError
    -> не возвращать null
```

Коротко:

```text
cancellation -> throw
known infrastructure failure -> UI error
bug/programming failure -> throw
```

---

# 2. НЕ принимать механический partial fix

Запрещено:

```csharp
catch (OperationCanceledException)
{
    throw;
}
catch (Exception)
{
    _dynamicError = "...";
    return null;
}
```

Такой код всё ещё маскирует `InvalidOperationException`, `ArgumentException`, `NullReferenceException`, mapping bugs и любые новые programming exceptions.

CGFR2 должен убрать blanket handling unexpected exceptions.

---

# 3. Сначала определить РЕАЛЬНЫЙ DB exception contract

Перед изменением production-кода обязательно пройти путь:

```text
ClayGridSharedParamsData.LoadSharedParamsAsync(...)
    ->
DbManager / RunDbAsync / RunAsync
    ->
Dapper / DbConnection
    ->
ISqlErrorHandler
```

Нужно выяснить, какой exact exception type реально выходит наружу при ожидаемой DB/connectivity ошибке.

Проверить актуальные реализации:

```text
ClayGridSharedParamsData
DbManager
RunDbAsync / RunAsync
ISqlErrorHandler
```

Не угадывать.

В отчёте обязательно написать:

```text
Expected DB failure type(s): ...
Почему именно эти type(s) являются фактическим infrastructure contract: ...
```

Если contract неясен — НЕ ставить `catch (Exception)`. Лучше оставить DB failure propagating и отдельно сообщить, чем снова скрыть programming bug.

---

# 4. Предпочтительная catch structure

После определения реального contract структура должна быть примерно:

```csharp
try
{
    sharedParams = await ClayGridSharedParamsData.LoadSharedParamsAsync(...);
}
catch (OperationCanceledException)
{
    throw;
}
catch (EXPECTED_DB_EXCEPTION ex)
{
    _dynamicError =
        $"Не удалось загрузить общие настройки №{sharedId}. " +
        "База данных недоступна.";

    // logging только по существующим conventions проекта
    return null;
}
```

И всё.

НЕ добавлять ниже:

```csharp
catch (Exception)
{
    ...
}
```

Unexpected exceptions должны пройти наружу автоматически.

---

# 5. Что считать expected DB failure

Не кодировать список наугад.

В зависимости от текущего DALC contract это может быть:

```text
DbException
SqlException
custom DALC/database exception
```

Но ловить можно только то, что соответствует фактическому коду.

Если DALC преобразует provider exception — ловить итоговый тип.

Если общий contract реально `DbException` — допустимо ловить его.

Если проект принципиально использует только `SqlException` — не расширять до всех `Exception`.

---

# 6. Cancellation обязательно отдельно

Отдельный regression test обязателен для:

```csharp
OperationCanceledException
```

`TaskCanceledException` наследуется от него, поэтому отдельный catch для него не нужен.

Cancellation — control flow, а не ошибка shared link.

Не должно происходить:

```text
user leaves page
component disposal cancellation
request cancellation
    ->
"База данных недоступна"
```

---

# 7. Programming exception должен быть видимым

Обязательный test:

```csharp
throw new InvalidOperationException("boom");
```

Ожидание:

```text
InvalidOperationException выходит наружу
_dynamicError не подменяет exception
```

Проверить type и message:

```csharp
var ex = Assert.Throws<InvalidOperationException>(...);
Assert.Equal("boom", ex.Message);
```

Этот тест принципиален: без него агент легко оставит broad catch.

---

# 8. Не смешивать invalid link и DB unavailable

Сейчас DB exception path пишет:

```text
"Ссылка недействительна или база данных недоступна."
```

Это неверно, потому что invalid semantic cases уже отдельно обрабатываются.

Уже есть:

### Empty result

```csharp
if (sharedParams.Count == 0)
{
    _dynamicError =
        $"Общие настройки №{sharedId} не найдены. " +
        "Возможно, ссылка устарела или была удалена.";
    return null;
}
```

### Wrong grid

```csharp
if (!ClaySharedParamValidator.IsValid(sharedParams.Keys, paramNames))
{
    _dynamicError =
        $"Общие настройки №{sharedId} не соответствуют текущему гриду. " +
        "Ссылка могла быть создана для другого набора данных.";
    return null;
}
```

Поэтому expected DB failure должен говорить только про infrastructure failure, например:

```text
"Не удалось загрузить общие настройки №{sharedId}. База данных недоступна."
```

---

# 9. Semantic validation branches сохранить

Не менять поведение:

```text
sharedParams.Count == 0
    -> terminal user-facing "не найдены"
    -> return null

validator false
    -> terminal user-facing "не соответствуют текущему гриду"
    -> return null
```

Это normal semantic validation, а не exceptions.

---

# 10. Не трогать shared-state application

Не менять без необходимости:

```text
ApplySharedParams(...)
ClaySharedParamValidator
ClayGridParamRegistry
ResolveSharedId
_isSharedMode
_hasSharedSettings
shared URL parsing
```

CGFR2 — только exception boundary загрузки shared params.

---

# 11. Не трогать CGFR1 lifecycle

Не менять:

```text
ClayGridDynamicKey
_currentDynamicKey
OnParametersSetAsync
ResetDynamicRuntimeState
URI-aware Query
same-instance reinit
```

Важное взаимодействие:

```text
OperationCanceledException/programming exception
    ->
LoadAndValidateSharedParamsAsync propagates
    ->
InitDynamicMode throws
    ->
OnParametersSetAsync catch из CGFR1.1
    ->
_currentDynamicKey = null
    ->
exception propagates
```

Это правильный contract.

Не добавлять catch, который ломает эту цепочку.

---

# 12. Expected DB failure path

При expected DB failure:

```text
LoadSharedParamsAsync throws expected DB exception
    ->
LoadAndValidateSharedParamsAsync catches it
    ->
sets _dynamicError
    ->
returns null
```

Но агент обязан проверить дальнейший путь.

Сейчас после:

```csharp
var sharedParams =
    await LoadAndValidateSharedParamsAsync(sharedId.Value, opt);

if (sharedParams is not null)
    await ApplySharedParams(sharedParams, opt);
```

ниже всё равно находится:

```csharp
await NotifyQueryChanged();
```

Комментарий утверждает:

```text
Если null — _dynamicError уже установлен, грид не загрузится
```

Нужно проверить, правда ли это.

---

# 13. Обязательный audit: грузятся ли данные после shared error

Проверить:

```text
NotifyQueryChanged()
LoadDynamicData()
_dynamicError guards
```

Вариант A:

```text
_dynamicError != null уже блокирует main data load
```

Тогда ничего не менять, показать guard в отчёте.

Вариант B:

```text
shared load -> null
_dynamicError set
InitDynamicMode всё равно вызывает NotifyQueryChanged
main grid SELECT выполняется
```

Тогда это связанный correctness bug. Исправить минимально:

```csharp
if (_dynamicError is not null)
    return;
```

в правильной точке перед first data load.

И добавить regression:

```text
expected shared DB failure
=> main grid data SELECT не выполняется
```

Не делать этот fix без подтверждения кодом.

---

# 14. Tests обязательны

Минимум четыре ветки:

```text
1. cancellation propagates
2. expected DB failure -> _dynamicError + no propagation
3. programming exception propagates
4. semantic empty/not-found behavior unchanged
```

Желательно также wrong-grid validation branch.

---

# 15. Использовать существующую fake DB infrastructure

Не подключаться к real SQL Server.

Использовать уже созданные:

```text
bUnit
ScriptedConnection
ScriptedCommand
ScriptEntry
DbManager fake connection factory
CommandLog
```

После CGFR1.4 fake DB умеет:

```text
lazy dequeue
scripted exception
retry after exception
CommandLog
```

Не создавать второй fake DB framework.

---

# 16. Предпочтительны component-level bUnit tests

`LoadAndValidateSharedParamsAsync` private и вызывается внутри real dynamic initialization.

Поэтому предпочтительно идти через настоящий:

```text
ClayGrid<ClayDynamicRow>
```

и URL вида:

```text
?id=<gridId>&CLID=<clid>&sharedId=<sharedId>
```

с scripted DB.

Не вызывать private method reflection, если lifecycle path можно собрать через bUnit.

---

# 17. Test: cancellation propagates

Сценарий:

```text
definition loads
columns load
state load
shared mode active
shared params DB call throws OperationCanceledException
```

Ожидание:

```text
OperationCanceledException выходит наружу
```

Проверять конкретно:

```csharp
Assert.Throws<OperationCanceledException>(...);
```

или async equivalent.

Не использовать:

```csharp
catch { }
```

---

# 18. Test: programming exception propagates

Shared params DB call:

```csharp
new InvalidOperationException("boom")
```

Ожидание:

```csharp
var ex = Assert.Throws<InvalidOperationException>(...);
Assert.Equal("boom", ex.Message);
```

Не должно быть `_dynamicError` вместо exception.

---

# 19. Test: expected DB failure -> UI error

Использовать exact type, определённый в §3.

Если contract = `DbException`, можно сделать test subclass:

```csharp
internal sealed class TestDbException : DbException
{
    public TestDbException(string message) : base(message) { }
}
```

Если contract другой — адаптировать.

Ожидание:

```text
DB exception НЕ выходит наружу
_dynamicError установлен
сообщение говорит про недоступность БД
shared params не применяются
```

Если audit §13 показал, что data load должен быть заблокирован, проверить:

```text
main data SELECT count == 0
```

---

# 20. Не создавать SqlException reflection hack без необходимости

Если expected DB contract можно тестировать через `DbException` или custom DALC exception — использовать это.

Не создавать provider exception fragile reflection-ом.

---

# 21. Test: empty shared params

Shared load возвращает empty dictionary.

Ожидание:

```text
_dynamicError содержит "не найдены"
normal terminal UI result
exception нет
```

Это доказывает, что semantic behavior не сломано.

---

# 22. Test: wrong-grid params

Shared load возвращает dictionary, для которого:

```text
ClaySharedParamValidator.IsValid(...) == false
```

Ожидание:

```text
_dynamicError содержит "не соответствуют текущему гриду"
exception нет
```

Если existing test уже надёжно покрывает это — можно не дублировать, но в отчёте указать имя existing test.

---

# 23. Logging

Следовать существующему project convention.

Если expected DB failure уже логируется через:

```text
ISqlErrorHandler
DbManager
Debug.WriteLine
ILogger
```

не вводить новый logging framework.

Не логировать cancellation как error.

Не проглатывать programming exception после логирования.

---

# 24. Проверить double reporting

Если DALC/DbManager уже вызывает `ISqlErrorHandler` при DB exception, проверить, не создаёт ли Grid второй user-facing popup/notification.

В отчёте написать:

```text
кто отвечает за SQL error reporting:
DbManager / ISqlErrorHandler / ClayGrid
```

и почему новый catch не даёт double-reporting.

---

# 25. Не менять DALC без необходимости

CGFR2 — Grid boundary.

Не вносить изменения в `Clayzor.Lib.DALC`, если текущий DALC contract достаточен.

Если обнаружен отдельный DALC bug — описать отдельно, не смешивать автоматически в CGFR2.

---

# 26. НЕ трогать lookup catches

Рядом в `ClayGrid.Dynamic.cs` есть:

```csharp
catch (OperationCanceledException) { throw; }
catch (Exception ex)
{
    Debug.WriteLine(...);
}
```

для List/Icon lookup.

Это CGFR3.

Не исправлять сейчас.

---

# 27. НЕ трогать JS catches

JS lifecycle/dispose exception boundaries — CGFR4.

Не смешивать с CGFR2.

---

# 28. Что НЕ принимать

Не принимать:

```csharp
catch
{
}
```

Не принимать:

```csharp
catch (Exception)
{
}
```

Не принимать:

```csharp
catch (OperationCanceledException) { throw; }
catch (Exception) { _dynamicError = ...; }
```

Не принимать только cancellation test.

Не принимать отсутствие programming-exception test.

Не принимать real SQL Server dependency.

Не принимать `Thread.Sleep` / magic `Task.Delay`.

Не принимать reflection lifecycle invocation без крайней необходимости.

Не принимать изменения lookup/JS boundaries в этом commit.

---

# 29. Acceptance criteria — production

- [ ] В `LoadAndValidateSharedParamsAsync` больше нет bare `catch`.
- [ ] Нет blanket `catch (Exception)` на shared-load boundary.
- [ ] `OperationCanceledException` propagates.
- [ ] Unexpected `InvalidOperationException` propagates.
- [ ] Expected DB/connectivity exception обрабатывается отдельным exact catch.
- [ ] Expected DB failure устанавливает user-facing `_dynamicError`.
- [ ] DB error message не говорит, что ссылка invalid.
- [ ] Empty shared params по-прежнему дают "не найдены".
- [ ] Invalid param set по-прежнему даёт "не соответствуют текущему гриду".
- [ ] CGFR1 lifecycle code не изменён без необходимости.
- [ ] Lookup catches не изменены.
- [ ] JS catches не изменены.

---

# 30. Acceptance criteria — tests

- [ ] Regression: `OperationCanceledException` propagates.
- [ ] Regression: `InvalidOperationException("boom")` propagates.
- [ ] Regression: expected DB exception -> `_dynamicError`, no propagation.
- [ ] Regression: empty shared params -> existing not-found result.
- [ ] Желательно: wrong-grid validation regression.
- [ ] Нет real SQL Server.
- [ ] Нет blanket catch в tests.
- [ ] Нет magic delays.
- [ ] Existing CGFR1.x lifecycle tests остаются зелёными.

---

# 31. Обязательный ответ про first data load

В отчёте отдельно ответить:

```text
После expected shared DB failure выполняется ли NotifyQueryChanged/main data load?
```

Если нет — показать существующий guard.

Если да — показать добавленный минимальный guard и regression test, что main data SELECT не выполняется.

---

# 32. AGENTS.md

Обновить:

```text
Components/Grid/AGENTS.md
```

Зафиксировать:

```text
Shared settings exception boundary:

OperationCanceledException -> propagate.
Expected DB/connectivity exception -> terminal user-facing dynamic error.
Unexpected/programming exceptions -> propagate.

Semantic errors "not found" / "wrong grid" — normal terminal UI results,
не exceptions.

Blanket catch на shared-load boundary запрещён.
```

---

# 33. Рекомендуемый production shape

Только ориентир:

```csharp
private async Task<IReadOnlyDictionary<string, string>?> LoadAndValidateSharedParamsAsync(
    int sharedId,
    ClayGridDynamicSettings opt)
{
    var paramNames =
        ClayGridParamRegistry.GetGridParamNames(opt, _dynamicGridId);

    IReadOnlyDictionary<string, string> sharedParams;

    try
    {
        sharedParams =
            await ClayGridSharedParamsData.LoadSharedParamsAsync(
                Db,
                sharedId,
                opt.UserParamsShared,
                paramNames);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (EXPECTED_DATABASE_EXCEPTION)
    {
        _dynamicError =
            $"Не удалось загрузить общие настройки №{sharedId}. " +
            "База данных недоступна.";
        return null;
    }

    if (sharedParams.Count == 0)
    {
        _dynamicError =
            $"Общие настройки №{sharedId} не найдены. " +
            "Возможно, ссылка устарела или была удалена.";
        return null;
    }

    if (!ClaySharedParamValidator.IsValid(sharedParams.Keys, paramNames))
    {
        _dynamicError =
            $"Общие настройки №{sharedId} не соответствуют текущему гриду. " +
            "Ссылка могла быть создана для другого набора данных.";
        return null;
    }

    return sharedParams;
}
```

`EXPECTED_DATABASE_EXCEPTION` — placeholder. Определить реальный type перед кодированием.

---

# 34. Финальный отчёт агента

Обязательно написать:

1. какой exact exception type ловится как expected DB failure;
2. почему выбран именно он;
3. что происходит с `OperationCanceledException`;
4. что происходит с `InvalidOperationException`;
5. что происходит с expected DB exception;
6. выполняется ли main data load после shared DB failure;
7. кто отвечает за DB error reporting и нет ли double reporting;
8. production files;
9. test files;
10. названия новых tests;
11. `dotnet test` result;
12. `dotnet build` result.

---

# 35. Коммиты

Если production и tests в разных repositories — два отдельных commit.

Production:

```text
CGFR2: narrow shared settings exception boundary
```

Tests:

```text
CGFR2: cover shared settings exception contract
```

Не смешивать CGFR3/CGFR4.
