using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Domain.Identity;
using HrSuite.Core.Modularity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

/// <summary>
/// Switches for Layer 3 licensing and Layer 4 connections. Both are rows, so an
/// implementation engineer turns a module or an integration on and off without a build
/// (acceptance scenarios 6 and 7).
/// </summary>
[Route("api/admin")]
[RequirePermission(Permissions.AdminTenant)]
public sealed class TenantAdminController : ApiControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ITenantModuleService _modules;
    private readonly ITenantIntegrationService _integrations;

    public TenantAdminController(ITenantModuleService modules, ITenantIntegrationService integrations)
    {
        _modules = modules;
        _integrations = integrations;
    }

    // ---------------- Layer 3: modules ----------------

    [HttpGet("modules")]
    public async Task<IActionResult> Modules(CancellationToken ct)
        => Data(await _modules.GetModuleStatesAsync(ct));

    [HttpPost("modules")]
    public async Task<IActionResult> SetModule([FromBody] ModuleToggleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ModuleKey))
            return Fail(ErrorCode.Validation, "A module key is required.");

        await _modules.SetEnabledAsync(request.ModuleKey, request.IsEnabled, ct);
        return Data(await _modules.GetModuleStatesAsync(ct));
    }

    // ---------------- Layer 4: integrations ----------------

    [HttpGet("integration")]
    public async Task<IActionResult> Integrations(CancellationToken ct)
        => Data(await _integrations.GetAllAsync(ct));

    [HttpGet("integration/{key}")]
    public async Task<IActionResult> Integration(string key, CancellationToken ct)
    {
        var found = await _integrations.GetAsync(key, ct);
        if (found is null) return Data(new { IntegrationKey = key, IsEnabled = false, Settings = (object?)null });

        return Data(new
        {
            found.IntegrationKey,
            found.IsEnabled,
            Settings = ParseSettings(found.SettingsJson)
        });
    }

    [HttpPost("integration")]
    public async Task<IActionResult> SaveIntegration([FromBody] IntegrationSaveRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IntegrationKey))
            return Fail(ErrorCode.Validation, "An integration key is required.");

        var saved = await _integrations.SaveAsync(request.IntegrationKey, request.SettingsJson, request.IsEnabled, ct);
        return saved is null ? Fail(ErrorCode.NotFound, "Integration not found.") : Data(saved);
    }

    private static object? ParseSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed class ModuleToggleRequest
    {
        public string ModuleKey { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }

    public sealed class IntegrationSaveRequest
    {
        public string IntegrationKey { get; set; } = string.Empty;
        public string? SettingsJson { get; set; }
        public bool IsEnabled { get; set; }
    }
}
