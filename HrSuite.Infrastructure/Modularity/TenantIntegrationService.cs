using HrSuite.Core.Abstractions;
using HrSuite.Core.Modularity;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Modularity;

/// <summary>Per-tenant integration settings, read from sys_tenant_integration.</summary>
public sealed class TenantIntegrationService : RepositoryBase, ITenantIntegrationService
{
    public TenantIntegrationService(IDbConnectionFactory factory, ITenantContext tenant)
        : base(factory, tenant) { }

    public async Task<TenantIntegration?> GetAsync(string integrationKey, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<IntegrationRow>(
            "sp_sys_tenant_integration_get",
            ProcArgs.New().Set("integration_key", integrationKey),
            ct).ConfigureAwait(false);

        return row is null ? null : new TenantIntegration(row.IntegrationKey, row.SettingsJson, row.IsEnabled);
    }

    public async Task<IReadOnlyList<TenantIntegration>> GetEnabledAsync(CancellationToken ct = default)
    {
        var rows = await QueryAsync<IntegrationRow>("sp_sys_tenant_integration_list", ct: ct).ConfigureAwait(false);

        return rows.Where(r => r.IsEnabled)
                   .Select(r => new TenantIntegration(r.IntegrationKey, r.SettingsJson, r.IsEnabled))
                   .ToList();
    }

    public async Task<IReadOnlyList<TenantIntegration>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await QueryAsync<IntegrationRow>("sp_sys_tenant_integration_list", ct: ct).ConfigureAwait(false);
        return rows.Select(r => new TenantIntegration(r.IntegrationKey, r.SettingsJson, r.IsEnabled)).ToList();
    }

    public async Task<TenantIntegration?> SaveAsync(
        string integrationKey, string? settingsJson, bool isEnabled, CancellationToken ct = default)
    {
        var row = await ExecuteReturningAsync<IntegrationRow>(
            "sp_sys_tenant_integration_set",
            ProcArgs.New()
                .Set("integration_key", integrationKey)
                .Set("settings_json", settingsJson)
                .Set("is_enabled", isEnabled),
            ct).ConfigureAwait(false);

        return row is null ? null : new TenantIntegration(row.IntegrationKey, row.SettingsJson, row.IsEnabled);
    }

    private sealed class IntegrationRow
    {
        public string IntegrationKey { get; set; } = string.Empty;
        public string? SettingsJson { get; set; }
        public bool IsEnabled { get; set; }
    }
}
