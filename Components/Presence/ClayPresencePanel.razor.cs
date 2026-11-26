using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Clayzor.Lib.Web.Controls.Components.Presence;

/// <summary>Показывает состояние присутствия других сотрудников в текущей карточке.</summary>
public partial class ClayPresencePanel : IAsyncDisposable
{
    /// <summary>Подтверждённая запись для интерактивного присутствия. Null скрывает панель.</summary>
    [Parameter, EditorRequired]
    public ClayPresenceTarget? Target { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private ILogger<ClayPresencePanel> Logger { get; set; } = null!;

    private ClayPresenceCircuit? _circuit;
    private ClayPresenceTarget? _current;
    private CancellationTokenSource? _generation;
    private readonly HashSet<Task> _workers = [];
    private IReadOnlyList<ClayPresenceAccess>? _participants;
    private bool _unavailable;
    private bool _disposed;

    private string Status => _participants is null
        ? _unavailable ? "Не удалось получить сведения о присутствии" : "Подключение к сведениям о присутствии…"
        : _participants.Count == 0 ? "Других редакторов сейчас нет" : "С записью работают…";

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (_current != Target)
        {
            _current = Target;
            Invalidate(unavailable: false);
        }
    }

    /// <inheritdoc />
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            _circuit = Services.GetServices<CircuitHandler>().OfType<ClayPresenceCircuit>().SingleOrDefault();
            if (_circuit is not null)
                _circuit.Changed += ConnectionChanged;
            Authentication.AuthenticationStateChanged += AuthenticationChanged;
        }

        if (_disposed || _current is not { } target || _generation is not null || _circuit is { Connected: false })
            return;

        var registry = Services.GetService<IClayPresenceRegistry>();
        var options = Services.GetService<ClayPresenceOptions>();
        var clock = Services.GetService<TimeProvider>();
        if (_circuit is null || registry is null || options is null || clock is null)
        {
            if (!_unavailable)
            {
                _unavailable = true;
                StateHasChanged();
            }
            return;
        }

        _generation = new CancellationTokenSource();
        _workers.RemoveWhere(task => task.IsCompleted);
        _workers.Add(WatchAsync(target, registry, options, clock, _generation));
    }

    private async Task WatchAsync(ClayPresenceTarget target, IClayPresenceRegistry registry,
        ClayPresenceOptions options, TimeProvider clock, CancellationTokenSource generation)
    {
        var token = generation.Token;
        var poll = new ClayPresencePoll(Authentication, registry);
        using var timer = new PeriodicTimer(options.HeartbeatInterval, clock);
        try
        {
            do
            {
                ClayPresenceSnapshot? snapshot = null;
                try
                {
                    snapshot = await poll.UpdateAsync(target, token);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    Logger.LogDebug("Не удалось подтвердить присутствие ({ErrorType}).", ex.GetType().Name);
                    await ReleaseAsync(poll);
                }

                token.ThrowIfCancellationRequested();
                _participants = snapshot?.Participants;
                _unavailable = snapshot is null;
                StateHasChanged();
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (token.IsCancellationRequested)
        {
            // Отменённый серверный вызов может завершиться с ошибкой источника данных.
            Logger.LogDebug("Завершение проверки присутствия ({ErrorType}).", ex.GetType().Name);
        }
        finally
        {
            await ReleaseAsync(poll);
            generation.Dispose();
        }
    }

    private async ValueTask ReleaseAsync(ClayPresencePoll poll)
    {
        try { await poll.LeaveAsync(); }
        catch (Exception ex)
        {
            Logger.LogDebug("Не удалось снять присутствие ({ErrorType}).", ex.GetType().Name);
        }
    }

    private void ConnectionChanged() => _ = InvokeAsync(() => Restart(_circuit?.Connected != true));

    private void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(() => Restart(unavailable: false));

    private void Restart(bool unavailable)
    {
        if (_disposed)
            return;

        Invalidate(unavailable);
        StateHasChanged();
    }

    private void Invalidate(bool unavailable)
    {
        var previous = _generation;
        _generation = null;
        previous?.Cancel();
        _participants = null;
        _unavailable = unavailable;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_circuit is not null)
            _circuit.Changed -= ConnectionChanged;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        Invalidate(unavailable: true);
        await Task.WhenAll(_workers);
    }
}

