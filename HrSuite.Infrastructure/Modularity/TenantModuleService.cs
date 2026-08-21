using HrSuite.Core.Abstractions;
using HrSuite.Core.Modularity;
using HrSuite.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;

namespace HrSuite.Infrastructure.Modularity;

/// <summary>
/// Which add-ons this tenant is licensed for. Read from sys_tenant_module, cached briefly
/// so the check on every request is cheap. Flipping a row takes effect within the cache window
/// and needs no build and no restart.
/// </summary>
public sealed class TenantModuleService : RepositoryBase, ITenantModuleService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;
    private readonly IModuleRegistry _registry;
    private readonly ITenantContext _tenant;

    public TenantModuleService(
        IDbConnectionFactory factory,
        ITenantContext tenant,
        IMemoryCache cache,
        IModuleRegistry registry)
        : base(factory, tenant)
    {
        _tenant = tenant;
        _cache = cache;
        _registry = registry;
    }

    public async Task<IReadOnlyList<string>> GetEnabledModuleKeysAsync(CancellationToken ct = default)
    {
        var key = $"tenant-modules:{_tenant.TenantId}";

        if (_cache.TryGetValue(key, out IReadOnlyList<string>? cached) && cached is not null) return cached;

        var rows = await QueryAsync<TenantModuleRow>("sp_sys_tenant_module_list", ct: ct).ConfigureAwait(false);

        var enabled = rows.Where(r => r.IsEnabled)
                          .Select(r => r.ModuleKey)
                          .Where(_registry.IsRegistered)   // a licensed key with no deployed assembly is inert
                          .ToList();

        _cache.Set(key, (IReadOnlyList<string>)enabled, CacheFor);
        return enabled;
    }

    public async Task<bool> IsEnabledAsync(string moduleKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleKey)) return true; // always-on infrastructure
        var enabled = await GetEnabledModuleKeysAsync(ct).ConfigureAwait(false);
        return enabled.Contains(moduleKey, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<MenuEntry>> GetMenuForTenantAsync(CancellationToken ct = default)
    {
        var enabled = await GetEnabledModuleKeysAsync(ct).ConfigureAwait(false);
        var modules = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);

        return _registry.AllMenuEntries
            .Where(m => m.ModuleKey is null || modules.Contains(m.ModuleKey))
            .Where(m => m.RequiredPermission is null || _tenant.Has(m.RequiredPermission))
            .ToList();
    }

    public async Task<IReadOnlyList<TenantModuleState>> GetModuleStatesAsync(CancellationToken ct = default)
    {
        var rows = await QueryAsync<TenantModuleRow>("sp_sys_tenant_module_list", ct: ct).ConfigureAwait(false);
        var enabled = rows.Where(r => r.IsEnabled)
                          .Select(r => r.ModuleKey)
                          .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Driven by what is deployed, not by what happens to have a row: a module with no
        // licence row yet still appears, switched off.
        return _registry.All
            .Where(m => m.ModuleKey is not null)
            .Select(m => new TenantModuleState(m.ModuleKey!, m.DisplayName, enabled.Contains(m.ModuleKey!)))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SetEnabledAsync(string moduleKey, bool isEnabled, CancellationToken ct = default)
    {
        await ExecuteAsync(
            "sp_sys_tenant_module_set",
            ProcArgs.New().Set("module_key", moduleKey).Set("is_enabled", isEnabled),
            ct).ConfigureAwait(false);

        _cache.Remove($"tenant-modules:{_tenant.TenantId}");
    }

    private sealed class TenantModuleRow
    {
        public string ModuleKey { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }
}
