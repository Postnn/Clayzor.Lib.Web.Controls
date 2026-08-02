using Clayzor.Lib.Web.Controls.Components.Grid.Dynamic;
using Clayzor.Lib.Web.Controls.Components.Tree;
using Clayzor.Lib.Web.Controls.Components.Tree.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clayzor.Lib.Web.Controls.Services;

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

    /// <summary>Регистрирует сервисы компонента ClayTreeView.</summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="config">Корневая конфигурация приложения.</param>
    public static IServiceCollection AddClayTree(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ClayTreeDynamicSettings>(config.GetSection("ClayTree:Dynamic"));
        services.AddSingleton<IValidateOptions<ClayTreeDynamicSettings>, ValidateClayTreeDynamicSettings>();
        services.AddScoped<IClayTreeStateStore, ClayTreeMemoryStateStore>();
        return services;
    }
}

/// <summary>
/// Валидатор <see cref="ClayTreeDynamicSettings"/> при старте приложения.
/// Вызывает <see cref="ClayTreeDynamicSettings.Validate"/> при первом резолве опций.
/// </summary>
internal sealed class ValidateClayTreeDynamicSettings : IValidateOptions<ClayTreeDynamicSettings>
{
    public ValidateOptionsResult Validate(string? name, ClayTreeDynamicSettings options)
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
