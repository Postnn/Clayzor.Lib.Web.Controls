using System.Security.Claims;
using Clayzor.Lib.Web.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Clayzor.Lib.Web.Controls.Themes;

/// <summary>Регистрация состояния темы и общего SQL-хранилища личного выбора.</summary>
public static class ClayThemeServiceCollectionExtensions
{
    /// <summary>Подключает scoped тему к общим настройкам Инвентаризации.</summary>
    /// <param name="services">Сервисы приложения.</param>
    /// <param name="getUser">Текущий пользователь из доверенного провайдера данного circuit.</param>
    public static IServiceCollection AddInventoryThemePreferences(this IServiceCollection services,
        Func<IServiceProvider, Task<ClaimsPrincipal>> getUser) =>
        services.AddInventoryThemePreferences(getUser, scope => scope.GetRequiredService<ClayAppSettings>());

    /// <summary>Подключает отдельный источник настроек темы для приложения с другой бизнес-базой.</summary>
    public static IServiceCollection AddInventoryThemePreferences(this IServiceCollection services,
        Func<IServiceProvider, Task<ClaimsPrincipal>> getUser,
        Func<IServiceProvider, ClayAppSettings> getSettings)
    {
        services.AddScoped<ClayThemeState>();
        services.AddScoped<IClayThemePreferenceStore>(scope => new InventoryThemePreferenceStore(
            () => getUser(scope), getSettings(scope)));
        return services;
    }
}
