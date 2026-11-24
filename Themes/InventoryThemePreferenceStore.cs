using System.Security.Claims;
using Clayzor.Lib.DALC;
using Clayzor.Lib.Entities.Inventory;
using Clayzor.Lib.Web.Settings;
using Microsoft.Data.SqlClient;

namespace Clayzor.Lib.Web.Controls.Themes;

/// <summary>Адресует личные настройки только после проверки аутентифицированного сотрудника.</summary>
public sealed class InventoryThemePreferenceStore(
    Func<Task<ClaimsPrincipal>> getUser,
    ClayAppSettings settings,
    string parameter) : IClayThemePreferenceStore, IDisposable
{
    private DbManager? _db;
    private string? _principal;
    private int? _employeeId;

    /// <summary>Загружает тему текущего проверенного сотрудника.</summary>
    /// <param name="cancellationToken">Токен отмены загрузки.</param>
    public async Task<string?> LoadAsync(CancellationToken cancellationToken) =>
        await ClayThemeSettingsData.LoadAsync(await GetTrustedDbAsync(cancellationToken), parameter, cancellationToken);

    /// <summary>Сохраняет тему текущего проверенного сотрудника.</summary>
    /// <param name="value">Сериализованное сочетание темы и режима.</param>
    /// <param name="cancellationToken">Токен отмены сохранения.</param>
    public async Task SaveAsync(string value, CancellationToken cancellationToken) =>
        await ClayThemeSettingsData.SaveAsync(await GetTrustedDbAsync(cancellationToken), parameter, value, cancellationToken);

    private async Task<DbManager> GetTrustedDbAsync(CancellationToken cancellationToken)
    {
        var principal = await GetPrincipalAsync();
        if (string.IsNullOrWhiteSpace(principal) || principal.EndsWith("\\SQLservice", StringComparison.OrdinalIgnoreCase) ||
            _principal is not null && !string.Equals(principal, _principal, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Контекст сотрудника для настройки темы не подтверждён.");

        var db = _db ??= OpenSettingsDb();
        var employee = await Employee.GetCurrentAsync(db, principal, cancellationToken);
        if (employee is null || _employeeId is not null && _employeeId != employee.Id ||
            !string.Equals(principal, await GetPrincipalAsync(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Контекст сотрудника для настройки темы не подтверждён.");

        _principal = principal;
        _employeeId = employee.Id;
        return db;
    }

    private async Task<string?> GetPrincipalAsync()
    {
        var identity = (await getUser()).Identity;
        return identity?.IsAuthenticated == true ? identity.Name : null;
    }

    private DbManager OpenSettingsDb()
    {
        var connection = new SqlConnectionStringBuilder(settings.ConnectionString);
        if (!connection.IntegratedSecurity || !string.Equals(connection.InitialCatalog, "Инвентаризация", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Для настройки темы требуется доверенное подключение к Инвентаризации.");

        // Ошибка оформления обрабатывается отдельно и не открывает блокирующий CRUD reconnect overlay.
        return new DbManager(settings.ConnectionString);
    }

    /// <summary>Освобождает отдельное подключение к настройкам.</summary>
    public void Dispose() => _db?.Dispose();
}
