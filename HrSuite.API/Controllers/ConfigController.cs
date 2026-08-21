using HrSuite.Core.Abstractions;
using HrSuite.Core.Modularity;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

/// <summary>
/// Layer 2, server half. One call at start-up carries everything that varies per tenant:
/// settings, field rules, the menu, the licensed add-ons and the client-side hook scripts.
/// </summary>
[Route("api/config")]
public sealed class ConfigController : ApiControllerBase
{
    private readonly IConfigResolver _config;
    private readonly ITenantModuleService _modules;
    private readonly ITenantIntegrationService _integrations;
    private readonly IHookEngine _hooks;
    private readonly ITenantContext _tenant;

    public ConfigController(
        IConfigResolver config,
        ITenantModuleService modules,
        ITenantIntegrationService integrations,
        IHookEngine hooks,
        ITenantContext tenant)
    {
        _config = config;
        _modules = modules;
        _integrations = integrations;
        _hooks = hooks;
        _tenant = tenant;
    }

    [HttpGet("bootstrap")]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        var settings = await _config.GetAllSettingsAsync(ct);
        var rules = await _config.GetAllFieldRulesAsync(ct);
        var modules = await _modules.GetEnabledModuleKeysAsync(ct);
        var menu = await _modules.GetMenuForTenantAsync(ct);
        var integrations = await _integrations.GetEnabledAsync(ct);
        var clientHooks = await _hooks.GetClientScriptsAsync(ct);

        return EnvelopeData(new
        {
            _tenant.TenantId,
            _tenant.TenantCode,
            _tenant.TenantName,
            Settings = settings.Values.Select(s => new { s.Key, s.Value, s.DataType }),
            FieldRules = rules.Select(r => new
            {
                r.ScreenKey,
                r.FieldKey,
                r.IsVisible,
                r.IsRequired,
                r.Label,
                r.SeqNo
            }),
            Menu = menu.Select(m => new
            {
                m.Key,
                m.Label,
                m.Route,
                m.Icon,
                m.SeqNo,
                m.ModuleKey
            }),
            EnabledModules = modules,
            EnabledIntegrations = integrations.Select(i => i.IntegrationKey),
            ClientHooks = clientHooks.Select(h => new
            {
                h.HookId,
                h.HookKey,
                h.SeqNo,
                h.ScriptBody,
                h.DebounceMs
            })
        });
    }
}
