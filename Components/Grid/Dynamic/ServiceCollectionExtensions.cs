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
    /// Регистрирует <see cref="ClayGridDynamicOptions"/> из конфигурации и валидатор опций.
    /// </summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="config">Корневая конфигурация приложения.</param>
    /// <param name="section">Путь к секции конфигурации (по умолчанию "ClayGrid:Dynamic").</param>
    public static IServiceCollection AddClayGridDynamic(
        this IServiceCollection services,
        IConfiguration config,
        string section = "ClayGrid:Dynamic")
    {
        services.Configure<ClayGridDynamicOptions>(config.GetSection(section));
        services.AddSingleton<IValidateOptions<ClayGridDynamicOptions>, ValidateClayGridDynamicOptions>();
        return services;
    }
}

/// <summary>
/// Валидатор <see cref="ClayGridDynamicOptions"/> при старте приложения.
/// Вызывает <see cref="ClayGridDynamicOptions.Validate"/> при первом резолве опций.
/// </summary>
internal sealed class ValidateClayGridDynamicOptions : IValidateOptions<ClayGridDynamicOptions>
{
    public ValidateOptionsResult Validate(string? name, ClayGridDynamicOptions options)
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
