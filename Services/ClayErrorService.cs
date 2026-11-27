using Clayzor.Lib.DALC;
using Microsoft.Data.SqlClient;

namespace Clayzor.Lib.Web.Controls;

/// <summary>
/// Глобальный сервис ошибок приложения. Регистрируется как Scoped.
/// Реализует <see cref="ISqlErrorHandler"/> — <see cref="Clayzor.Lib.DALC.DbManager"/> вызывает
/// <see cref="HandleSqlError"/> автоматически при любом <see cref="SqlException"/>.
/// Компонент <see cref="Components.ClayErrorBar"/> отображает баннер с детализацией.
/// </summary>
public class ClayErrorService : ISqlErrorHandler
{
    private Exception? _exception;

    /// <summary>Тип, сообщение и стек исключения для раскрываемых деталей.</summary>
    public string? ExceptionDetails => _exception?.ToString();

    /// <summary>Показывать детали операции также при сбое соединения.</summary>
    public bool HasOperationContext { get; private set; }

    /// <summary>Есть ли активная ошибка для отображения.</summary>
    public bool HasError { get; private set; }

    /// <summary>Текст ошибки (ex.Message).</summary>
    public string ErrorMessage { get; private set; } = string.Empty;

    /// <summary>Строка подключения к БД.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Подписи SQL-команд.</summary>
    public List<string> CommandLabels { get; private set; } = [];

    /// <summary>Тексты SQL-команд (с параметрами-плейсхолдерами).</summary>
    public List<string> CommandTexts { get; private set; } = [];

    /// <summary>Параметры SQL-запроса (имя, значение).</summary>
    public List<(string Name, object? Value)> Parameters { get; private set; } = [];

    /// <summary>Включён ли показ деталей (переключатель пользователя).</summary>
    public bool ShowDebug { get; set; }

    /// <summary>Является ли текущая ошибка ошибкой недоступности БД (connectivity).</summary>
    public bool IsCurrentErrorConnectivity { get; private set; }

    /// <summary>Событие изменения состояния — <see cref="Components.ClayErrorBar"/> подписывается для перерисовки.</summary>
    public event Action? OnChanged;

    /// <summary>Событие потери соединения с БД — <see cref="ClayDbReconnectService"/> подписывается для запуска переподключения.</summary>
    public event Action? OnConnectionLost;

    /// <inheritdoc />
    public void HandleSqlError(SqlException exception, string connectionString, string commandText, IReadOnlyList<(string Name, object? Value)> parameters)
    {
        _exception = exception;
        HasOperationContext = false;
        ConnectionString = connectionString;
        ErrorMessage = exception.Message;
        CommandLabels = ["Запрос"];
        CommandTexts = [commandText];
        Parameters = parameters.ToList();
        ShowDebug = false;
        HasError = true;

        // Классификация: connectivity-ошибка или обычная ошибка запроса?
        IsCurrentErrorConnectivity = DbManager.IsConnectivityError(exception);
        if (IsCurrentErrorConnectivity)
            OnConnectionLost?.Invoke();

        OnChanged?.Invoke();
    }

    /// <summary>Публикует сообщение операции и сохраняет детали того же SQL-исключения.</summary>
    public void ReportOperationFailure(string message, Exception exception)
    {
        if (!ReferenceEquals(_exception, exception))
        {
            CommandLabels = [];
            CommandTexts = [];
            Parameters = [];
            IsCurrentErrorConnectivity = exception is SqlException sql && DbManager.IsConnectivityError(sql);
        }
        ConnectionString = string.Empty;
        _exception = exception;
        ErrorMessage = message;
        HasError = true;
        HasOperationContext = true;
        ShowDebug = false;
        OnChanged?.Invoke();
    }

    /// <summary>Скрывает баннер ошибки.</summary>
    public void Clear()
    {
        _exception = null;
        HasOperationContext = false;
        HasError = false;
        ErrorMessage = string.Empty;
        ShowDebug = false;
        IsCurrentErrorConnectivity = false;
        OnChanged?.Invoke();
    }
}
