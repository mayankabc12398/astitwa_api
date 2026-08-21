using System.Text.RegularExpressions;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Extensions.Engine.Data;

public sealed class NamedQueryRepository : RepositoryBase
{
    /// <summary>
    /// Defence in depth. proc_name is already a registered value an administrator typed, but
    /// it reaches Dapper as a command name, so it is shape-checked before it gets there.
    /// </summary>
    private static readonly Regex ProcNamePattern = new("^sp_[A-Za-z0-9_]{1,140}$", RegexOptions.Compiled);

    public NamedQueryRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<NamedQueryDefinition>> ListAsync(PageRequest page, CancellationToken ct)
        => QueryPagedAsync<NamedQueryDefinition>("sp_ext_named_query_list", page, ct: ct);

    public Task<NamedQueryDefinition?> GetAsync(int queryId, CancellationToken ct)
        => QuerySingleAsync<NamedQueryDefinition>(
            "sp_ext_named_query_get", ProcArgs.New().Set("query_id", queryId), ct);

    public Task<NamedQueryDefinition?> GetByKeyAsync(string queryKey, CancellationToken ct)
        => QuerySingleAsync<NamedQueryDefinition>(
            "sp_ext_named_query_get_by_key", ProcArgs.New().Set("query_key", queryKey), ct);

    public Task<NamedQueryDefinition?> SaveAsync(
        NamedQuerySaveRequest request, string paramsJson, string columnsJson, bool allowGlobal, CancellationToken ct)
        => ExecuteReturningAsync<NamedQueryDefinition>(
            "sp_ext_named_query_save",
            ProcArgs.New()
                .Set("query_id", request.QueryId)
                .Set("query_key", request.QueryKey)
                .Set("proc_name", request.ProcName)
                .Set("params_json", paramsJson)
                .Set("columns_json", columnsJson)
                .Set("max_rows", request.MaxRows)
                .Set("required_permission", request.RequiredPermission)
                .Set("is_active", request.IsActive)
                .Set("apply_to_all_tenants", allowGlobal && request.ApplyToAllTenants),
            ct);

    public Task DeleteAsync(int queryId, CancellationToken ct)
        => ExecuteAsync("sp_ext_named_query_delete", ProcArgs.New().Set("query_id", queryId), ct);

    /// <summary>
    /// Runs a registered procedure. The tenant and the acting user are stamped by the base
    /// class exactly as for any other call, so a named query cannot escape its tenant.
    /// </summary>
    public Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteRegisteredAsync(
        string procName, ProcArgs args, CancellationToken ct)
    {
        if (!ProcNamePattern.IsMatch(procName))
            throw new InvalidOperationException($"'{procName}' is not a valid procedure name.");

        return QueryDynamicAsync(procName, args, ct);
    }

    public static bool IsValidProcName(string? procName)
        => !string.IsNullOrWhiteSpace(procName) && ProcNamePattern.IsMatch(procName);
}
