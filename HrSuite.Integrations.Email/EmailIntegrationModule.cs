using HrSuite.Core.Abstractions;
using HrSuite.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HrSuite.Integrations.Email;

/// <summary>
/// Layer 4 integration. Registers an INotificationChannel implementation and nothing else.
/// Enabled per tenant via sys_tenant_integration.integration_key = 'email.smtp'.
/// </summary>
public sealed class EmailIntegrationModule : IPluginModule
{
    public const string Key = "email.smtp";

    public string? ModuleKey => null; // integrations are enabled, not licensed as modules
    public string DisplayName => "SMTP Email";
    public ModuleLayer Layer => ModuleLayer.Integration;
    public int SeqNo => 400;

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // Added, not replaced: the dispatcher picks whichever registered channel the tenant
        // has switched on, and finds none when nothing is enabled.
        services.AddScoped<INotificationChannel, SmtpNotificationChannel>();
    }

    /// <summary>
    /// An integration contributes its own settings screen. The host filters this by the
    /// admin.tenant permission, so an ordinary user never sees it.
    /// </summary>
    public IEnumerable<MenuEntry> MenuEntries => new[]
    {
        new MenuEntry("integration.email", "Email Integration", "/admin/integrations/email", "mail", 930, null, "admin.tenant")
    };
}
