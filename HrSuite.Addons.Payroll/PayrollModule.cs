using HrSuite.Addons.Payroll.Controllers;
using HrSuite.Addons.Payroll.Data;
using HrSuite.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HrSuite.Addons.Payroll;

/// <summary>
/// Layer 3 add-on. Discovered by assembly scan — no Layer 1 project names this type.
/// Activation is per tenant via sys_tenant_module.module_key = 'payroll'.
/// </summary>
public sealed class PayrollModule : IPluginModule
{
    public const string Key = "payroll";

    public string? ModuleKey => Key;
    public string DisplayName => "Payroll";
    public ModuleLayer Layer => ModuleLayer.Addon;
    public int SeqNo => 300;

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<PayrollRunRepository>();
    }

    /// <summary>
    /// Menu entries are filtered by the host against the tenant's licence and the user's
    /// permissions, so a disabled module simply is not in the menu the client receives.
    /// </summary>
    public IEnumerable<MenuEntry> MenuEntries => new[]
    {
        new MenuEntry("payroll.runs", "Payroll Runs", "/payroll/runs", "wallet", 300, Key, PayrollPermissions.View)
    };
}
