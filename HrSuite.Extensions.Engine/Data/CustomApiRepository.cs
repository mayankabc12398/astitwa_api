using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Extensions.Engine.Data;

/// <summary>
/// Storage for the endpoints themselves. Ordinary repository work: stored procedures only,
/// tenant-stamped by the base class, exactly like every other table in the product.
///
/// The SQL an endpoint CARRIES is a different matter and is not run from here — see
/// CustomApiRunner, which is the only place in the product that executes text.
/// </summary>
public sealed class CustomApiRepository : RepositoryBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CustomApiRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<CustomApiEndpoint>> ListAsync(PageRequest page, CancellationToken ct)
        => QueryPagedAsync<CustomApiEndpoint>("sp_ext_api_list", page, ct: ct);

    public Task<CustomApiEndpoint?> GetAsync(int endpointId, CancellationToken ct)
        => QuerySingleAsync<CustomApiEndpoint>(
            "sp_ext_api_get", ProcArgs.New().Set("endpoint_id", endpointId), ct);

    public Task<CustomApiEndpoint?> GetBySlugAsync(string slug, CancellationToken ct)
        => QuerySingleAsync<CustomApiEndpoint>(
            "sp_ext_api_get_by_slug", ProcArgs.New().Set("slug", slug), ct);

    public async Task<bool> SlugTakenAsync(string slug, int endpointId, CancellationToken ct)
        => await ScalarAsync<int>(
            "sp_ext_api_slug_taken",
            ProcArgs.New().Set("slug", slug).Set("endpoint_id", endpointId),
            ct).ConfigureAwait(false) > 0;

    public Task<CustomApiEndpoint?> SaveAsync(
        CustomApiSaveRequest request, string paramsJson, string columnsJson, bool allowGlobal, CancellationToken ct)
        => ExecuteReturningAsync<CustomApiEndpoint>(
            "sp_ext_api_save",
            ProcArgs.New()
                .Set("endpoint_id", request.EndpointId)
                .Set("slug", request.Slug)
                .Set("title", request.Title)
                .Set("http_method", request.HttpMethod)
                .Set("sql_text", request.SqlText)
                .Set("params_json", paramsJson)
                .Set("columns_json", columnsJson)
                .Set("max_rows", request.MaxRows)
                .Set("required_permission", request.RequiredPermission)
                .Set("is_active", request.IsActive)
                .Set("apply_to_all_tenants", allowGlobal && request.ApplyToAllTenants),
            ct);

    public Task<CustomApiEndpoint?> SetActiveAsync(int endpointId, bool isActive, CancellationToken ct)
        => ExecuteReturningAsync<CustomApiEndpoint>(
            "sp_ext_api_set_active",
            ProcArgs.New().Set("endpoint_id", endpointId).Set("is_active", isActive),
            ct);

    public Task DeleteAsync(int endpointId, CancellationToken ct)
        => ExecuteAsync("sp_ext_api_delete", ProcArgs.New().Set("endpoint_id", endpointId), ct);

    public Task<IReadOnlyList<CustomApiVersion>> HistoryAsync(int endpointId, CancellationToken ct)
        => QueryAsync<CustomApiVersion>(
            "sp_ext_api_history_list", ProcArgs.New().Set("endpoint_id", endpointId), ct);

    public Task<CustomApiEndpoint?> RollbackAsync(int endpointId, int historyId, CancellationToken ct)
        => ExecuteReturningAsync<CustomApiEndpoint>(
            "sp_ext_api_rollback",
            ProcArgs.New().Set("endpoint_id", endpointId).Set("history_id", historyId),
            ct);

    public Task<PagedResult<CustomApiCallLogEntry>> LogAsync(PageRequest page, CancellationToken ct)
        => QueryPagedAsync<CustomApiCallLogEntry>("sp_ext_api_log_list", page, ct: ct);

    public Task LogCallAsync(
        int? endpointId, string slug, string status, int durationMs, int rowCount, string? message, CancellationToken ct)
        => ExecuteAsync(
            "sp_ext_api_log_insert",
            ProcArgs.New()
                .Set("endpoint_id", endpointId)
                .Set("slug", slug)
                .Set("status", status)
                .Set("duration_ms", durationMs)
                .Set("row_count", rowCount)
                .Set("message", message),
            ct);

    // -----------------------------------------------------------------
    // The two JSON columns, read in one place so a malformed one behaves the same
    // everywhere: as an empty list rather than as a failed request.
    // -----------------------------------------------------------------

    public static IReadOnlyList<CustomApiParam> ParamsOf(CustomApiEndpoint endpoint)
        => Deserialise<List<CustomApiParam>>(endpoint.ParamsJson) ?? new List<CustomApiParam>();

    public static IReadOnlyList<string> ColumnsOf(CustomApiEndpoint endpoint)
        => Deserialise<List<string>>(endpoint.ColumnsJson) ?? new List<string>();

    public static T? Deserialise<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
