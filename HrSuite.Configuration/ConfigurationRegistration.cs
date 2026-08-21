using HrSuite.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HrSuite.Configuration;

public static class ConfigurationRegistration
{
    /// <summary>
    /// Layer 2 wiring. The host references this project because base code needs a live
    /// IConfigResolver from the very first request; layers 3, 4 and 5 are discovered instead.
    /// </summary>
    public static IServiceCollection AddHrSuiteConfiguration(this IServiceCollection services)
    {
        services.AddScoped<ConfigRepository>();
        services.AddScoped<ConfigResolver>();
        services.AddScoped<IConfigResolver>(sp => sp.GetRequiredService<ConfigResolver>());
        services.AddScoped<ITemplateRenderer, TemplateRenderer>();
        return services;
    }
}
