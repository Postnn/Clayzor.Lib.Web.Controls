using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;

/// <summary>
/// Методы расширения для регистрации сервисов динамического режима ClayGrid в DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="ClayGridDynamicSettings"/> из конфигурации и валидатор опций.
    /// </summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="config">Корневая конфигурация приложения.</param>
    /// <param name="section">Путь к секции конфигурации (по умолчанию "ClayGrid:Dynamic").</param>
    public static IServiceCollection AddClayGridDynamic(
        this IServiceCollection services,
        IConfiguration config,
        string section = "ClayGrid:Dynamic")
    {
        services.Configure<ClayGridDynamicSettings>(config.GetSection(section));
        services.AddSingleton<IValidateOptions<ClayGridDynamicSettings>, ValidateClayGridDynamicSettings>();
        return services;
    }
}

/// <summary>
/// Валидатор <see cref="ClayGridDynamicSettings"/> при старте приложения.
/// Вызывает <see cref="ClayGridDynamicSettings.Validate"/> при первом резолве опций.
/// </summary>
internal sealed class ValidateClayGridDynamicSettings : IValidateOptions<ClayGridDynamicSettings>
{
    public ValidateOptionsResult Validate(string? name, ClayGridDynamicSettings options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}
