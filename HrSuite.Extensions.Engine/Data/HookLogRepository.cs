using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Extensions.Engine.Data;

public sealed class HookLogRepository : RepositoryBase
{
    private const int MessageLimit = 4000;
    private const int ContextLimit = 8000;

    public HookLogRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task WriteAsync(
        int? hookId, string hookKey, string runOn, string status, int durationMs,
        string? message, string? contextJson, CancellationToken ct)
        => ExecuteAsync(
            "sp_ext_hook_log_insert",
            ProcArgs.New()
                .Set("hook_id", hookId)
                .Set("hook_key", hookKey)
                .Set("run_on", runOn)
                .Set("status", status)
                .Set("duration_ms", durationMs)
                .Set("message", Trim(message, MessageLimit))
                .Set("context_json", Trim(contextJson, ContextLimit)),
            ct);

    public Task<PagedResult<HookLogEntry>> ListAsync(PageRequest page, string? status, int? hookId, CancellationToken ct)
        => QueryPagedAsync<HookLogEntry>(
            "sp_ext_hook_log_list",
            page,
            ProcArgs.New()
                .Set("status", string.IsNullOrWhiteSpace(status) ? null : status)
                .Set("hook_id", hookId),
            ct);

    /// <summary>A runaway script must not be able to fill the log table with one entry.</summary>
    private static string? Trim(string? value, int limit)
        => value is null || value.Length <= limit ? value : value[..limit] + "…";
}
