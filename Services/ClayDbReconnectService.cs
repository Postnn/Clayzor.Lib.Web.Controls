using Clayzor.Lib.DALC;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.SqlClient;

namespace Clayzor.Lib.Web.Controls;

/// <summary>
/// Состояние переподключения к БД.
/// </summary>
public enum ReconnectState
{
    /// <summary>Нет активной ошибки соединения.</summary>
    Idle,
    /// <summary>Выполняется попытка переподключения.</summary>
    Reconnecting,
    /// <summary>Все попытки исчерпаны, восстановление невозможно.</summary>
    Failed,
}

/// <summary>
/// Scoped-сервис авто-переподключения к SQL Server.
/// Подписывается на <see cref="ClayErrorService.OnConnectionLost"/> и выполняет
/// до <see cref="MaxAttempts"/> попыток health-check с интервалом <see cref="RetryIntervalSeconds"/>.
/// </summary>
public class ClayDbReconnectService : IDisposable
{
    /// <summary>Максимальное число попыток переподключения.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Интервал между попытками в секундах.</summary>
    public const int RetryIntervalSeconds = 30;

    private readonly ClayErrorService _errorService;
    private readonly DbManager _db;
    private readonly NavigationManager _nav;
    private CancellationTokenSource? _cts;

    /// <summary>Текущее состояние переподключения.</summary>
    public ReconnectState State { get; private set; } = ReconnectState.Idle;

    /// <summary>Текущий номер попытки (1-based, валидно при <see cref="State"/> == <see cref="ReconnectState.Reconnecting"/>).</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Текст последней ошибки.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>Событие изменения состояния — UI-компонент подписывается и вызывает <c>InvokeAsync(StateHasChanged)</c>.</summary>
    public event Action? StateChanged;

    public ClayDbReconnectService(ClayErrorService errorService, DbManager db, NavigationManager nav)
    {
        _errorService = errorService;
        _db = db;
        _nav = nav;
        _errorService.OnConnectionLost += OnConnectionLost;
    }

    private void OnConnectionLost()
    {
        // Защита от повторного запуска, если уже в процессе
        if (State == ReconnectState.Reconnecting)
            return;

        AttemptCount = 0;
        State = ReconnectState.Reconnecting;
        StateChanged?.Invoke();

        // Запускаем цикл переподключения (fire-and-forget с CancellationToken для безопасной отмены)
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = ReconnectLoopAsync(_cts.Token);
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        try
        {
            while (AttemptCount < MaxAttempts && !ct.IsCancellationRequested)
            {
                AttemptCount++;
                StateChanged?.Invoke();

                var healthOk = await TryHealthCheckAsync(ct);
                if (healthOk)
                {
                    // Успех — полная перезагрузка страницы для пересоздания компонентов
                    _nav.NavigateTo(_nav.Uri, forceLoad: true);
                    return;
                }

                if (AttemptCount < MaxAttempts && !ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            if (!ct.IsCancellationRequested)
            {
                State = ReconnectState.Failed;
                StateChanged?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // Компонент уничтожен — ничего не делаем
        }
        catch (Exception ex)
        {
            // Неожиданная ошибка в цикле — переходим в Failed
            LastError = ex.Message;
            State = ReconnectState.Failed;
            StateChanged?.Invoke();
        }
    }

    private async Task<bool> TryHealthCheckAsync(CancellationToken ct)
    {
        try
        {
            // После изменений в DbManager (Шаг 2) при связь-ошибке ExecuteScalarAsync
            // вернёт default (null для int?), а не выбросит исключение.
            var result = await _db.ExecuteScalarAsync<int?>("SELECT 1",
                commandType: System.Data.CommandType.Text);
            return result == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Сбрасывает состояние в Idle (вызывается при старте новой страницы).
    /// </summary>
    public void Reset()
    {
        _cts?.Cancel();
        _cts = null;
        State = ReconnectState.Idle;
        AttemptCount = 0;
        LastError = string.Empty;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _errorService.OnConnectionLost -= OnConnectionLost;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
