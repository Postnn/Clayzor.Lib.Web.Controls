namespace Clayzor.Lib.Web.Controls.Components.Presence;

/// <summary>Общие интервалы heartbeat и ограниченного времени жизни регистрации.</summary>
public sealed class ClayPresenceOptions
{
    /// <summary>Интервал повторного получения списка и проверки доступа.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Максимальный срок сохранения регистрации без heartbeat.</summary>
    public TimeSpan LeaseLifetime { get; set; } = TimeSpan.FromSeconds(45);
}

/// <summary>Результат подтверждённого heartbeat.</summary>
/// <param name="Lease">Токен, уникальный для поколения регистрации вкладки.</param>
/// <param name="Participants">Снимок других сотрудников; null означает восстановление источника.</param>
public sealed record ClayPresenceSnapshot(Guid Lease, IReadOnlyList<ClayPresenceAccess>? Participants);

/// <summary>Серверное хранилище аренды для вызовов после проверки доступа.</summary>
public interface IClayPresenceRegistry
{
    /// <summary>Создаёт новую аренду либо обновляет указанное поколение и возвращает актуальный список.</summary>
    /// <returns>Null, если указанное поколение истекло или больше не соответствует сотруднику и записи.</returns>
    ValueTask<ClayPresenceSnapshot?> HeartbeatAsync(
        ClayPresenceKey key, ClayPresenceAccess access, Guid? lease, CancellationToken cancellationToken);

    /// <summary>Снимает только указанное поколение аренды.</summary>
    ValueTask LeaveAsync(Guid lease);
}

internal sealed class ClayProcessPresenceRegistry(TimeProvider clock, ClayPresenceOptions options) : IClayPresenceRegistry
{
    private sealed record Registration(ClayPresenceKey Key, ClayPresenceAccess Access, long LastHeartbeat);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Registration> _registrations = [];
    private readonly long _startedAt = clock.GetTimestamp();

    public ValueTask<ClayPresenceSnapshot?> HeartbeatAsync(
        ClayPresenceKey key, ClayPresenceAccess access, Guid? lease, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = clock.GetTimestamp();
            foreach (var registration in _registrations.ToArray())
            {
                if (clock.GetElapsedTime(registration.Value.LastHeartbeat, now) >= options.LeaseLifetime)
                    _registrations.Remove(registration.Key);
            }

            if (lease is { } currentLease &&
                (!_registrations.TryGetValue(currentLease, out var current) ||
                 current.Key != key || current.Access.EmployeeId != access.EmployeeId))
                return ValueTask.FromResult<ClayPresenceSnapshot?>(null);

            var token = lease ?? Guid.NewGuid();
            _registrations[token] = new(key, access, now);
            if (clock.GetElapsedTime(_startedAt, now) < options.LeaseLifetime)
                return ValueTask.FromResult<ClayPresenceSnapshot?>(new(token, null));

            var participants = _registrations.Values
                .Where(item => item.Key == key && item.Access.EmployeeId != access.EmployeeId)
                .GroupBy(item => item.Access.EmployeeId)
                .Select(group => group.OrderByDescending(item => item.Access.Activity)
                    .ThenByDescending(item => item.LastHeartbeat).First().Access)
                .OrderBy(person => person.DisplayName, StringComparer.Ordinal)
                .ThenBy(person => person.EmployeeId)
                .ToArray();
            return ValueTask.FromResult<ClayPresenceSnapshot?>(new(token, participants));
        }
    }

    public ValueTask LeaveAsync(Guid lease)
    {
        lock (_gate)
            _registrations.Remove(lease);

        return ValueTask.CompletedTask;
    }
}
