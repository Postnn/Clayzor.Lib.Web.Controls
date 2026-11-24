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
        Func<IServiceProvider, Task<ClaimsPrincipal>> getUser)
    {
        services.AddScoped<ClayThemeState>();
        services.AddScoped<IClayThemePreferenceStore>(scope => new InventoryThemePreferenceStore(
            () => getUser(scope), scope.GetRequiredService<ClayAppSettings>()));
        return services;
    }
}
