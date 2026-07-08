# ClayErrorBar

Баннер ошибок БД. Автоматически отображается при любом `SqlException`, перехваченном `DbManager`. Размещается в `MainLayout` над `@Body`.

## Принцип работы

1. `DbManager` при `SqlException` вызывает `ISqlErrorHandler.HandleSqlError()` с полным SQL-текстом и параметрами
2. `ClayErrorService` (реализует `ISqlErrorHandler`) сохраняет ошибку и уведомляет UI
3. `ClayErrorBar` (в `MainLayout.razor`) отображает красный баннер с сообщением, кнопкой закрытия и переключателем детализации

## Состав детализации

При включении переключателя «▼ Детали запроса» показывается:

- **Строка подключения** — из `DbManager.ConnectionString`
- **SQL-текст** — именно тот запрос, который был отправлен на SQL Server (с параметрами-плейсхолдерами `@param`)
- **Параметры** — таблица с именами и значениями параметров, извлечённых из Dapper-объекта

## Регистрация

В `Program.cs`:

```csharp
builder.Services.AddScoped<ClayErrorService>();
builder.Services.AddScoped<ISqlErrorHandler>(sp => sp.GetRequiredService<ClayErrorService>());
builder.Services.AddScoped<DbManager>(sp => new DbManager(claySettings.ConnectionString, sp.GetRequiredService<ISqlErrorHandler>()));
```

В `MainLayout.razor`:

```razor
<MudMainContent Class="pa-6">
    <ClayErrorBar />
    @Body
</MudMainContent>
```

## Правила для страниц

- **НЕ вызывать** `ErrorService.Report()` вручную — `DbManager` делает это автоматически
- Достаточно `try/finally` для управления `_loading = false`:

```csharp
private async Task LoadData()
{
    _loading = true;
    try
    {
        _query.TotalCount = await MyEntity.GetCountAsync(Db, where, param);
        _items = (await MyEntity.GetPagedAsync(Db, where, orderBy, param, page, size)).ToList();
    }
    finally
    {
        _loading = false;
    }
}
```

## ClayErrorService

Scoped-сервис, реализует `ISqlErrorHandler`:

| Член | Описание |
|---|---|
| `HasError` | Есть ли активная ошибка |
| `ErrorMessage` | Текст ошибки (`SqlException.Message`) |
| `ConnectionString` | Строка подключения |
| `CommandLabels` | Метки SQL-команд |
| `CommandTexts` | Тексты SQL (с параметрами-плейсхолдерами) |
| `Parameters` | `(string Name, object? Value)` — параметры запроса |
| `ShowDebug` | Переключатель детализации |
| `OnChanged` | Событие для уведомления UI |
| `Clear()` | Скрыть баннер |

## ISqlErrorHandler (DALC)

Интерфейс в `Clayzor.Lib.DALC`:

```csharp
public interface ISqlErrorHandler
{
    void HandleSqlError(SqlException exception, string connectionString,
        string commandText, IReadOnlyList<(string Name, object? Value)> parameters);
}
```
