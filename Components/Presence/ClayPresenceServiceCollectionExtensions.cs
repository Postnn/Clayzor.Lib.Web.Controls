using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clayzor.Lib.Web.Controls.Components.Presence;

/// <summary>Регистрация общих служб присутствия в приложении Blazor Server.</summary>
public static class ClayPresenceServiceCollectionExtensions
{
    /// <summary>Подключает присутствие; хранилище по умолчанию действует в одном процессе.</summary>
    public static IServiceCollection AddClayEntityPresence(
        this IServiceCollection services, Action<ClayPresenceOptions>? configure = null)
    {
        var options = new ClayPresenceOptions();
        configure?.Invoke(options);
        if (options.HeartbeatInterval <= TimeSpan.Zero || options.LeaseLifetime <= options.HeartbeatInterval)
            throw new ArgumentOutOfRangeException(nameof(configure), "Срок аренды должен превышать интервал heartbeat.");

        services.TryAddSingleton(options);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IClayPresenceRegistry, ClayProcessPresenceRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, ClayPresenceCircuit>());
        return services;
    }
}

internal sealed class ClayPresenceCircuit : CircuitHandler
{
    internal bool Connected { get; private set; }
    internal event Action? Changed;

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Connected = true;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Connected = false;
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}
