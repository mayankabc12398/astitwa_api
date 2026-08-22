using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Identity;
using HrSuite.Extensions.Engine.Data;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Extensions.Engine.Runtime;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.Extensions.Engine.Controllers;

/// <summary>
/// The API Builder admin screen. Writing an endpoint here is the only way one comes into
/// existence, and it is gated on admin.extensions like every other Layer 5 surface.
///
/// Save refuses anything Test would have refused, because the screen's "test first" rule is
/// a workflow and workflows can be skipped — a request posted by hand cannot be.
/// </summary>
[ApiController]
[Route("api/admin/apis")]
[Produces("application/json")]
public sealed class CustomApiAdminController : ExtensionControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] Methods = { "GET", "POST" };

    private readonly CustomApiRepository _endpoints;
    private readonly CustomApiRunner _runner;
    private readonly ITenantContext _tenant;

    public CustomApiAdminController(CustomApiRepository endpoints, CustomApiRunner runner, ITenantContext tenant)
    {
        _endpoints = endpoints;
        _runner = runner;
        _tenant = tenant;
    }

    /// <summary>What the editor offers in its dropdowns, so the screen hardcodes none of it.</summary>
    [HttpGet("meta")]
    public IActionResult Meta() => Data(new
    {
        Methods,
        ParamTypes = SqlGuard.ParamTypes,
        Permissions = Permissions.All,
        SqlGuard.TenantToken,
        CanApplyToAllTenants = _tenant.Has(Permissions.AdminTenant)
    });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct)
        => Data(await _endpoints.ListAsync(page, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var found = await _endpoints.GetAsync(id, ct);
        if (found is null) return Fail(ErrorCode.NotFound, "Endpoint not found.");

        return Data(Shape(found));
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] CustomApiSaveRequest request, CancellationToken ct)
    {
        var slug = (request.Slug ?? string.Empty).Trim().ToLowerInvariant();

        if (!SqlGuard.IsValidSlug(slug))
        {
            return Fail(ErrorCode.Validation,
                "The address must be lowercase letters, digits and hyphens — for example employees-by-department.");
        }

        if (!Methods.Contains((request.HttpMethod ?? string.Empty).ToUpperInvariant()))
            return Fail(ErrorCode.Validation, "The method must be GET or POST.");

        if (request.MaxRows is < 1 or > 1000)
            return Fail(ErrorCode.Validation, "Max rows must be between 1 and 1000.");

        // An endpoint with no whitelist would answer with nothing at all, which reads as a
        // broken endpoint rather than as the safe default it is. Refuse it while the author
        // is still looking at the screen.
        if (request.Columns.Count == 0)
            return Fail(ErrorCode.Validation, "Choose at least one output column. An empty list returns nothing.");

        var verdict = SqlGuard.Check(request.SqlText, request.Params);
        if (!verdict.Ok) return Fail(ErrorCode.Validation, verdict.Error!);

        if (await _endpoints.SlugTakenAsync(slug, request.EndpointId, ct))
            return Fail(ErrorCode.Conflict, $"/api/x/{slug} is already in use.");

        var canWriteGlobal = _tenant.Has(Permissions.AdminTenant);
        if (request.ApplyToAllTenants && !canWriteGlobal)
            return Fail(ErrorCode.Forbidden, "Publishing an endpoint to every tenant needs the admin.tenant permission.");

        request.Slug = slug;
        request.HttpMethod = (request.HttpMethod ?? "POST").ToUpperInvariant();
        request.RequiredPermission = string.IsNullOrWhiteSpace(request.RequiredPermission)
            ? null
            : request.RequiredPermission.Trim();

        var paramsJson = JsonSerializer.Serialize(request.Params, Json);
        var columnsJson = JsonSerializer.Serialize(request.Columns, Json);

        var saved = await _endpoints.SaveAsync(request, paramsJson, columnsJson, canWriteGlobal, ct);
        return saved is null ? Fail(ErrorCode.NotFound, "Endpoint not found.") : Data(Shape(saved));
    }

    /// <summary>Runs the statement as written, without saving it. Nothing is stored.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] CustomApiTestRequest request, CancellationToken ct)
        => Data(await _runner.TestAsync(request, ct));

    [HttpPost("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromBody] ActiveRequest request, CancellationToken ct)
    {
        var current = await _endpoints.GetAsync(id, ct);
        if (current is null) return Fail(ErrorCode.NotFound, "Endpoint not found.");

        // Activating is publishing a URL. Re-check the statement first: it may have been
        // saved when a rule was looser, or edited in the database since.
        if (request.IsActive)
        {
            var verdict = SqlGuard.Check(current.SqlText, CustomApiRepository.ParamsOf(current));
            if (!verdict.Ok) return Fail(ErrorCode.Validation, verdict.Error!);
        }

        var updated = await _endpoints.SetActiveAsync(id, request.IsActive, ct);
        return updated is null ? Fail(ErrorCode.NotFound, "Endpoint not found.") : Data(Shape(updated));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _endpoints.DeleteAsync(id, ct);
        return Data(null);
    }

    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> History(int id, CancellationToken ct)
        => Data(await _endpoints.HistoryAsync(id, ct));

    [HttpPost("{id:int}/rollback/{historyId:int}")]
    public async Task<IActionResult> Rollback(int id, int historyId, CancellationToken ct)
    {
        var restored = await _endpoints.RollbackAsync(id, historyId, ct);
        return restored is null ? Fail(ErrorCode.NotFound, "Endpoint not found.") : Data(Shape(restored));
    }

    [HttpGet("log")]
    public async Task<IActionResult> Log([FromQuery] PageRequest page, CancellationToken ct)
        => Data(await _endpoints.LogAsync(page, ct));

    /// <summary>
    /// The two JSON columns are unpacked for the client, so the screen works with lists
    /// rather than with strings it would have to parse itself.
    /// </summary>
    private static object Shape(CustomApiEndpoint endpoint) => new
    {
        endpoint.EndpointId,
        endpoint.TenantId,
        endpoint.Slug,
        endpoint.Title,
        endpoint.HttpMethod,
        endpoint.SqlText,
        Params = CustomApiRepository.ParamsOf(endpoint),
        Columns = CustomApiRepository.ColumnsOf(endpoint),
        endpoint.MaxRows,
        endpoint.RequiredPermission,
        endpoint.IsActive,
        endpoint.VersionNo,
        endpoint.UpdatedOn
    };

    public sealed class ActiveRequest
    {
        public bool IsActive { get; set; }
    }
}
