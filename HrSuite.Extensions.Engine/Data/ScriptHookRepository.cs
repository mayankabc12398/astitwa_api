using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Extensions.Engine.Data;

/// <summary>
/// Layer 5 storage. Uses the same tenant-filtered repository base as base code, so a script
/// registered for one tenant can never be read — let alone run — for another.
/// </summary>
public sealed class ScriptHookRepository : RepositoryBase
{
    public ScriptHookRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<ScriptHook>> ListAsync(PageRequest page, string? runOn, CancellationToken ct)
        => QueryPagedAsync<ScriptHook>(
            "sp_ext_hook_list",
            page,
            ProcArgs.New().Set("run_on", string.IsNullOrWhiteSpace(runOn) ? null : runOn),
            ct);

    public Task<ScriptHook?> GetAsync(int hookId, CancellationToken ct)
        => QuerySingleAsync<ScriptHook>("sp_ext_hook_get", ProcArgs.New().Set("hook_id", hookId), ct);

    public Task<ScriptHook?> SaveAsync(ScriptHookSaveRequest request, bool allowGlobal, CancellationToken ct)
        => ExecuteReturningAsync<ScriptHook>(
            "sp_ext_hook_save",
            ProcArgs.New()
                .Set("hook_id", request.HookId)
                .Set("hook_key", request.HookKey)
                .Set("seq_no", request.SeqNo)
                .Set("run_on", request.RunOn)
                .Set("script_body", request.ScriptBody)
                .Set("debounce_ms", request.DebounceMs)
                .Set("is_active", request.IsActive)
                .Set("apply_to_all_tenants", allowGlobal && request.ApplyToAllTenants),
            ct);

    public Task<ScriptHook?> SetActiveAsync(int hookId, bool isActive, CancellationToken ct)
        => ExecuteReturningAsync<ScriptHook>(
            "sp_ext_hook_set_active",
            ProcArgs.New().Set("hook_id", hookId).Set("is_active", isActive),
            ct);

    public Task DeleteAsync(int hookId, CancellationToken ct)
        => ExecuteAsync("sp_ext_hook_delete", ProcArgs.New().Set("hook_id", hookId), ct);

    public Task<IReadOnlyList<ScriptHookVersion>> HistoryAsync(int hookId, CancellationToken ct)
        => QueryAsync<ScriptHookVersion>("sp_ext_hook_history_list", ProcArgs.New().Set("hook_id", hookId), ct);

    public Task<ScriptHook?> RollbackAsync(int hookId, int historyId, CancellationToken ct)
        => ExecuteReturningAsync<ScriptHook>(
            "sp_ext_hook_rollback",
            ProcArgs.New().Set("hook_id", hookId).Set("history_id", historyId),
            ct);

    /// <summary>What the engine actually runs for one slot.</summary>
    public Task<IReadOnlyList<ScriptHook>> ActiveForKeyAsync(string hookKey, string runOn, CancellationToken ct)
        => QueryAsync<ScriptHook>(
            "sp_ext_hook_active_for_key",
            ProcArgs.New().Set("hook_key", hookKey).Set("run_on", runOn),
            ct);

    /// <summary>Client scripts only. A server script body never leaves the server.</summary>
    public Task<IReadOnlyList<ClientHookScript>> ClientScriptsAsync(CancellationToken ct)
        => QueryAsync<ClientHookScript>("sp_ext_hook_client_scripts", ct: ct);
}
