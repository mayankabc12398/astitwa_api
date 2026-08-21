namespace HrSuite.Core.Modularity;

/// <summary>
/// What the host discovered at startup and what the current tenant is licensed for.
/// Populated by assembly scan; queried by base code without knowing any module type.
/// </summary>
public interface IModuleRegistry
{
    IReadOnlyList<ModuleDescriptor> All { get; }
    IReadOnlyList<MenuEntry> AllMenuEntries { get; }
    bool IsRegistered(string moduleKey);
}

public sealed record ModuleDescriptor(
    string? ModuleKey,
    string DisplayName,
    ModuleLayer Layer,
    string AssemblyName,
    int SeqNo);

public sealed record TenantModuleState(string ModuleKey, string DisplayName, bool IsEnabled);

/// <summary>Per-tenant activation, read from <c>sys_tenant_module</c>.</summary>
public interface ITenantModuleService
{
    Task<IReadOnlyList<string>> GetEnabledModuleKeysAsync(CancellationToken ct = default);
    Task<bool> IsEnabledAsync(string moduleKey, CancellationToken ct = default);
    Task<IReadOnlyList<MenuEntry>> GetMenuForTenantAsync(CancellationToken ct = default);

    /// <summary>Every registered module with this tenant's licence state, for the admin screen.</summary>
    Task<IReadOnlyList<TenantModuleState>> GetModuleStatesAsync(CancellationToken ct = default);

    /// <summary>Flips a licence. Takes effect within the cache window; no build, no restart.</summary>
    Task SetEnabledAsync(string moduleKey, bool isEnabled, CancellationToken ct = default);
}
