using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Identity;
using HrSuite.Extensions.Engine.Data;
using HrSuite.Extensions.Engine.Models;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.Extensions.Engine.Controllers;

/// <summary>
/// The Named Queries admin screen (section 10.6). Registering a key against a stored
/// procedure — with its declared parameters, its output whitelist and its row cap — is the
/// only way to widen what a script can read.
/// </summary>
[ApiController]
[Route("api/admin/queries")]
[Produces("application/json")]
public sealed class NamedQueryController : ExtensionControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NamedQueryRepository _queries;
    private readonly INamedQueryRunner _runner;
    private readonly ITenantContext _tenant;

    public NamedQueryController(NamedQueryRepository queries, INamedQueryRunner runner, ITenantContext tenant)
    {
        _queries = queries;
        _runner = runner;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct)
        => Data(await _queries.ListAsync(page, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var found = await _queries.GetAsync(id, ct);
        if (found is null) return Fail(ErrorCode.NotFound, "Query not found.");

        return Data(new
        {
            found.QueryId,
            found.TenantId,
            found.QueryKey,
            found.ProcName,
            Params = Deserialise<List<NamedQueryParam>>(found.ParamsJson) ?? new List<NamedQueryParam>(),
            Columns = Deserialise<List<string>>(found.ColumnsJson) ?? new List<string>(),
            found.MaxRows,
            found.RequiredPermission,
            found.IsActive
        });
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] NamedQuerySaveRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.QueryKey))
            return Fail(ErrorCode.Validation, "A query key is required.");

        if (!NamedQueryRepository.IsValidProcName(request.ProcName))
            return Fail(ErrorCode.Validation, "The procedure name must look like sp_area_action.");

        if (request.Columns.Count == 0)
            return Fail(ErrorCode.Validation, "Declare at least one output column. An empty whitelist exposes nothing.");

        if (request.MaxRows is < 1 or > 1000)
            return Fail(ErrorCode.Validation, "Max rows must be between 1 and 1000.");

        // p_tenant_id and p_user_id are stamped by the repository base on every call.
        // A registration must not try to own them.
        static string WithoutPrefix(string? name)
        {
            var value = name ?? string.Empty;
            return value.StartsWith("p_", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        }

        var reserved = request.Params
            .Select(p => WithoutPrefix(p.Name))
            .FirstOrDefault(n => n.Equals("tenant_id", StringComparison.OrdinalIgnoreCase)
                              || n.Equals("user_id", StringComparison.OrdinalIgnoreCase));

        if (reserved is not null)
            return Fail(ErrorCode.Validation, $"'{reserved}' is supplied by the tenant filter. Do not declare it.");

        var canWriteGlobal = _tenant.Has(Permissions.AdminTenant);
        if (request.ApplyToAllTenants && !canWriteGlobal)
            return Fail(ErrorCode.Forbidden, "Registering a query for every tenant needs the admin.tenant permission.");

        var paramsJson = JsonSerializer.Serialize(request.Params, Json);
        var columnsJson = JsonSerializer.Serialize(request.Columns, Json);

        var saved = await _queries.SaveAsync(request, paramsJson, columnsJson, canWriteGlobal, ct);
        return saved is null ? Fail(ErrorCode.NotFound, "Query not found.") : Data(saved);
    }

    /// <summary>Runs a registered query from the admin screen, through the same guarded runner.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] RunRequest request, CancellationToken ct)
        => Data(await _runner.RunAsync(request.QueryKey, request.Params, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _queries.DeleteAsync(id, ct);
        return Data(null);
    }

    private static T? Deserialise<T>(string? json) where T : class
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

    public sealed class RunRequest
    {
        public string QueryKey { get; set; } = string.Empty;
        public Dictionary<string, object?>? Params { get; set; }
    }
}
