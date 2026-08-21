using HrSuite.Core.Abstractions;
using HrSuite.Core.Modularity;
using HrSuite.Extensions.Engine.Data;
using HrSuite.Extensions.Engine.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HrSuite.Extensions.Engine;

/// <summary>
/// Layer 5. Supplies the Jint hook runner, the named-query runner and the hook audit log.
///
/// Base code depends only on IHookEngine and INamedQueryRunner, declared in Core. Registering
/// here replaces the Layer 1 null implementations, because the last registration wins — which
/// is exactly what "the engine is deployed" should mean.
/// </summary>
public sealed class ExtensionEngineModule : IPluginModule
{
    public const string Key = "extensions";

    public string? ModuleKey => null; // always on; individual scripts are the licensed unit
    public string DisplayName => "Extension Engine";
    public ModuleLayer Layer => ModuleLayer.Extension;
    public int SeqNo => 500;

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ScriptHookRepository>();
        services.AddScoped<NamedQueryRepository>();
        services.AddScoped<HookLogRepository>();

        services.AddScoped<INamedQueryRunner, NamedQueryRunner>();
        services.AddScoped<ScriptHost>();

        // Registered concretely as well, so the admin controller can reach Test() without
        // widening the interface base code depends on.
        services.AddScoped<JintHookEngine>();
        services.AddScoped<IHookEngine>(sp => sp.GetRequiredService<JintHookEngine>());
    }
}
