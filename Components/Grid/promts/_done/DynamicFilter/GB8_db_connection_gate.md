> Часть серии «GB — багфиксы по итогам тестирования». Перед началом прочитай **GB0_README_grid_ux_fixes.md** и **src/Clayzor.Lib.DALC/AGENTS.md**. **Блокирует приёмку GB1** — выполнять до повторной проверки GB1. Делай ТОЛЬКО этот шаг, затем сборка + тест + блок «Проверка».

# GB8 — «Подключение не поддерживает MultipleActiveResultSets»: два запроса на одном соединении

Прочитать перед началом: `src/Clayzor.Lib.DALC/DbManager.cs` — целиком (свойство `Connection`,
`QueryAsync`, `QueryStoredProcAsync`, `ExecuteScalarAsync`, `ExecuteAsync`, `Dispose`);
`src/Clayzor.Lib.DALC/AGENTS.md`; `src/Clayzor.Lib.Entities/DynamicGrid/DynamicSql.cs` — целиком
(**шесть** обращений к `db.Connection.*` в обход методов `DbManager`);
`src/Clayzor.Lib.Entities/Entity.cs` — как ходит в БД он (через методы `DbManager`, не через
`Connection`); `src/Kesco.App.Web.Inventory/Program.cs` — регистрация `DbManager` (`AddScoped`);
`src/Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.razor.cs` — `OnAfterRenderAsync`
(ленивая догрузка `_groupChildIds`, флаг `_loadingChildIds`);
`src/Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Grouping.cs` — `LoadChildIdsForGroupsAsync`;
`src/Clayzor.Lib.Web.Controls/Components/Grid/ClayGrid.Dynamic.Grouping.cs` —
`LoadDynamicGroupChildIdsAsync`, `LoadDynamicGroupedData`;
`src/Clayzor.Lib.Web.Controls/Services/ClayErrorService.cs` — что делает `HandleSqlError`
(важно: он вызывается ИЗ `catch` внутри `DbManager`).

## Дефект

Стек из отчёта тестировщика:

```
System.InvalidOperationException: Подключение не поддерживает MultipleActiveResultSets.
   at Dapper.SqlMapper.QueryAsync[T]
   at DynamicSql.QueryRowsAsync
   at ClayGrid`1.LoadDynamicGroupChildIdsAsync
   at ClayGrid`1.LoadChildIdsForGroupsAsync
   at ClayGrid`1.OnAfterRenderAsync
```

**GB1 этот дефект не создал — он его открыл.** GB1 включил режим выбора, а вместе с ним —
ветку `OnAfterRenderAsync`, которая ходит в БД:

```csharp
if (_selectMode && (Dynamic || DataLoader is not null) && !_loadingChildIds)
{
    …
    await LoadChildIdsForGroupsAsync(missingKeys);
}
```

Корень — устройство `DbManager`:

```csharp
public class DbManager : IDisposable
{
    private SqlConnection? _connection;      // ОДНО соединение на скоуп

    public SqlConnection Connection
    {
        get
        {
            if (_connection is null) _connection = new SqlConnection(_connectionString);
            if (_connection.State != ConnectionState.Open) _connection.Open();
            return _connection;
        }
    }
}
```

`DbManager` зарегистрирован `AddScoped`, а в Blazor Server скоуп — это **вся жизнь цепи (circuit)**,
а не короткий HTTP-запрос, как сказано в комментарии к классу. То есть на весь сеанс работы
пользователя — одно `SqlConnection`. `MultipleActiveResultSets` в строке подключения нет
(`Server=localhost;Database=Инвентаризация;Integrated Security=true;TrustServerCertificate=true;`).

Пока запросы идут строго по одному, это работает. Но `OnAfterRenderAsync` вызывается на КАЖДЫЙ
отрендеренный батч, в том числе на промежуточный `StateHasChanged()` внутри уже запущенного
обработчика события. Обработчик (например, `ToggleDynamicGroup` → `NotifyQueryChanged` →
`LoadDynamicGroupedData`) в этот момент ждёт свой `await` на том же соединении. Рендерер, не
дожидаясь его завершения, вызывает `OnAfterRenderAsync`, тот идёт в БД за ID потомков групп —
и получает второй одновременный запрос на одном соединении → исключение.

Флаг `_loadingChildIds` от этого не спасает: он защищает от повторного входа в СВОЮ ветку, а
конфликтует она с чужим запросом.

Отдельная деталь, из-за которой «починить в `DbManager.QueryAsync`» недостаточно: `DynamicSql`
**не пользуется методами** `DbManager`, он берёт `db.Connection` и работает с ним напрямую
(6 мест). Любая защита, встроенная только в методы `DbManager`, эти вызовы не покрывает.

Это не гонка потоков (circuit однопоточен) — это переиспользование занятого соединения.
Лечится сериализацией доступа к нему.

## Изменить/создать

### 1. `DbManager` — шлюз на единственное соединение

```csharp
/// <summary>
/// Сериализует доступ к единственному соединению скоупа. В Blazor Server скоуп живёт
/// столько же, сколько circuit, а рендерер может вызвать OnAfterRenderAsync посреди
/// уже запущенного обработчика — два await'а на одном SqlConnection без MARS дают
/// InvalidOperationException. Очередь дешевле, чем MARS: см. «Не делай».
/// </summary>
private readonly SemaphoreSlim _gate = new(1, 1);

/// <summary>
/// Выполняет операцию на соединении скоупа под шлюзом. Единственный законный способ
/// работать с <see cref="SqlConnection"/> снаружи DbManager.
/// Внутри действия нельзя вызывать другие методы DbManager — шлюз не реентерабельный.
/// Результат обязан быть буферизованным (Dapper по умолчанию buffered: true):
/// незакрытый reader после выхода из-под шлюза вернёт ту же ошибку.
/// </summary>
public async Task<T> RunAsync<T>(Func<SqlConnection, Task<T>> action)
{
    await _gate.WaitAsync();
    try
    {
        return await action(Connection);
    }
    finally
    {
        _gate.Release();
    }
}
```

Собственные методы `DbManager` (`QueryAsync`, `QueryStoredProcAsync`, `ExecuteScalarAsync`,
`ExecuteAsync`) перевести на `RunAsync`, сохранив `try/catch (SqlException)` →
`_errorHandler?.HandleSqlError(...)` ровно там, где он сейчас. Пример:

```csharp
public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null, int? commandTimeout = null)
{
    try
    {
        return await RunAsync(c => c.QueryAsync<T>(sql, parameters, commandTimeout: commandTimeout));
    }
    catch (SqlException ex)
    {
        _errorHandler?.HandleSqlError(ex, _connectionString, sql, ExtractParams(parameters));
        throw;
    }
}
```

`catch` — СНАРУЖИ `RunAsync`, а не внутри: `HandleSqlError` не должен выполняться под шлюзом.
Проверь, что `ClayErrorService.HandleSqlError` не ходит в БД; если ходит — это deadlock,
останавливайся и сообщай, не выкручивайся.

`Dispose`: добавить `_gate.Dispose()` после закрытия соединения.

Свойство `Connection` оставить публичным (на нём держится совместимость), но дописать в
`/// <summary>` предупреждение: выполнять запросы напрямую через него нельзя, только
через `RunAsync`.

### 2. `DynamicSql` — все шесть обращений через шлюз

```csharp
var rows = await db.RunAsync(c => c.QueryAsync(cmd));
```

Механически, по всем шести местам (`QueryRowsAsync`, `QueryPagedRowsAsync`, `ExecuteScalarAsync`,
`QueryPairsAsync`/`QueryTriplesAsync` и `ExecuteAsync` — сверься с файлом, список мест уточни
grep-ом `\.Connection\.`). Логику, SQL, сигнатуры, `CommandDefinition`, `CancellationToken`
не менять. `buffered: false` не появляется нигде — если увидишь, останавливайся.

После правки `grep -rn "\.Connection\." src --include=*.cs` должен давать попадания **только**
в `DbManager.cs`.

### 3. `src/Clayzor.Lib.DALC/AGENTS.md`

Прямое указание на правку документации. Добавить:

- `DbManager` — Scoped, **одно соединение на circuit** (не «на HTTP-запрос», как написано
  в комментарии класса сейчас; комментарий тоже поправь);
- MARS выключен намеренно; доступ к соединению сериализован `SemaphoreSlim`;
- **запросы выполняются только через методы `DbManager` или через `DbManager.RunAsync`**;
  `db.Connection.QueryAsync(...)` напрямую — запрещено;
- `ISqlErrorHandler` не имеет права обращаться в БД.

Другую документацию не трогать.

## Не делай

- **Не включай `MultipleActiveResultSets=True`.** Это не фикс: MARS не делает `SqlConnection`
  безопасным для параллельного использования, а превращает явную ошибку в плавающие
  «Connection is busy»/порчу состояния сессии; плюс требует правки строки подключения на всех
  стендах и в `web.config`, который держат админы.
- Не переводи `DbManager` на «соединение на каждый запрос» (create/open/dispose внутри метода).
  По существу это правильная модель для веб-приложения (пул сделает её дешёвой) и она снимает
  проблему совсем — но это смена контракта публичного `Connection`, `Dispose` и всей DALC.
  В багфикс не входит: запиши кандидатом в бэклог и оставь решение заказчику.
- Не регистрируй `DbManager` как Transient и не трогай `Program.cs` — Scoped-соединение под
  шлюзом работает, а смена времени жизни потянет за собой `ClayErrorService`.
- Не делай `_gate` статическим — шлюз на соединение, а соединений столько же, сколько цепей.
- Не заводи `lock`/`Monitor` — под ним нельзя `await`.
- Не трогай `ClayGrid.OnAfterRenderAsync`, `_loadingChildIds` и порядок загрузок: после
  сериализации второй запрос честно дождётся первого. Перенос загрузки ID потомков из
  рендера — отдельная тема (см. «Не входит в серию» в GB0), в этот шаг не входит.
- Не глотай исключения в `RunAsync` — он их только пропускает наверх.

## Проверка (ручная)

- `Kesco.App.Web.Inventory`, `?id=140`, «Выбрать записи» ВКЛ, группировка ВКЛ →
  разворачивать/сворачивать группы, листать страницы, менять сортировку, быстро кликать по
  нескольким группам подряд → исключения «Подключение не поддерживает MultipleActiveResultSets»
  нет ни в консоли сервера, ни в логах circuit;
- «выделить всё» в шапке при группировке (внутри — `LoadChildIdsForGroupsAsync`) → без ошибок;
- быстрый ввод в поиск (debounce 300 мс) при включённом режиме выбора и группировке → грид
  перезагружается, ошибок нет;
- «Групповые операции» → «Выгрузка в Excel» → «Все данные» во время листания страниц →
  ошибок нет, файл корректный;
- полный прогон блока «Проверка» из **GB1** — теперь должен пройти целиком;
- `/medical-tests` (статика): загрузка, группировка, выбор, печать, Excel, редактирование
  и удаление записи — работают как раньше;
- `grep -rn "\.Connection\." src --include=*.cs` → только `DbManager.cs`;
- `dotnet build Clayzor.sln` + `dotnet test tests\Clayzor.Lib.Web.Controls.Tests` — зелёные;
- в SQL-профайлере: число запросов на действие не выросло (шлюз не порождает запросы, только
  выстраивает их в очередь).
