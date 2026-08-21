namespace HrSuite.Core.Modularity;

/// <summary>Per-tenant integration settings, read from <c>sys_tenant_integration</c>.</summary>
public interface ITenantIntegrationService
{
    Task<TenantIntegration?> GetAsync(string integrationKey, CancellationToken ct = default);
    Task<IReadOnlyList<TenantIntegration>> GetEnabledAsync(CancellationToken ct = default);

    /// <summary>Every integration row for this tenant, enabled or not.</summary>
    Task<IReadOnlyList<TenantIntegration>> GetAllAsync(CancellationToken ct = default);

    Task<TenantIntegration?> SaveAsync(string integrationKey, string? settingsJson, bool isEnabled, CancellationToken ct = default);
}

public sealed record TenantIntegration(string IntegrationKey, string? SettingsJson, bool IsEnabled);
