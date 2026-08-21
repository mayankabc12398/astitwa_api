using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HrSuite.Core.Modularity;

/// <summary>
/// The one contract every optional assembly implements — add-ons (Layer 3),
/// integrations (Layer 4) and the extension engine (Layer 5) alike.
///
/// Core declares it and never names an implementation. The host discovers
/// implementations by scanning assemblies at startup, so no Layer 1 project
/// holds a compile-time reference to a Layer 3/4/5 type.
/// </summary>
public interface IPluginModule
{
    /// <summary>Matches <c>sys_tenant_module.module_key</c>. Null for always-on infrastructure.</summary>
    string? ModuleKey { get; }

    string DisplayName { get; }

    ModuleLayer Layer { get; }

    /// <summary>Load order. Lower runs first.</summary>
    int SeqNo => 100;

    void Register(IServiceCollection services, IConfiguration configuration);

    /// <summary>Menu entries this module contributes. Filtered per tenant by the host.</summary>
    IEnumerable<MenuEntry> MenuEntries => Array.Empty<MenuEntry>();
}

public enum ModuleLayer
{
    Configuration = 2,
    Addon         = 3,
    Integration   = 4,
    Extension     = 5
}
