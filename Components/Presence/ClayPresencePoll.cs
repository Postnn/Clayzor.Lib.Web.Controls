using Microsoft.AspNetCore.Components.Authorization;

namespace Clayzor.Lib.Web.Controls.Components.Presence;

internal sealed class ClayPresencePoll(AuthenticationStateProvider authentication, IClayPresenceRegistry registry)
{
    private Guid? _lease;
    private int? _employeeId;

    internal async ValueTask<ClayPresenceSnapshot?> UpdateAsync(ClayPresenceTarget target, CancellationToken token)
    {
        var principal = (await authentication.GetAuthenticationStateAsync().WaitAsync(token)).User;
        var access = principal.Identity?.IsAuthenticated == true
            ? await target.AuthorizeAsync(principal, token)
            : null;
        token.ThrowIfCancellationRequested();

        if (access is not { EmployeeId: > 0 } || string.IsNullOrWhiteSpace(access.DisplayName) ||
            !Enum.IsDefined(access.Activity))
        {
            await LeaveAsync();
            return null;
        }

        if (_employeeId != access.EmployeeId)
            await LeaveAsync();

        var snapshot = await registry.HeartbeatAsync(target.Key, access, _lease, token);
        if (snapshot is null && _lease is not null)
            snapshot = await registry.HeartbeatAsync(target.Key, access, null, token);

        _lease = snapshot?.Lease;
        _employeeId = access.EmployeeId;
        return snapshot;
    }

    internal async ValueTask LeaveAsync()
    {
        if (_lease is { } lease)
        {
            _lease = null;
            await registry.LeaveAsync(lease);
        }
    }
}
