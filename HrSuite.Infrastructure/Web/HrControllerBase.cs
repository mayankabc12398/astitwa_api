using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HrSuite.Infrastructure.Web;

/// <summary>
/// The envelope and status-code mapping, written once for every controller in the product —
/// base code and plugin assemblies alike.
///
/// It lives here rather than in HrSuite.API because add-on, integration and extension
/// assemblies must answer in the same envelope without referencing the host. A plugin
/// referencing the host would invert the dependency rule and make the build circular.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class HrControllerBase : ControllerBase
{
    protected string TraceId => HttpContext.TraceIdentifier;

    protected ITenantContext Tenant => HttpContext.RequestServices.GetRequiredService<ITenantContext>();

    protected IActionResult Data(object? data) => Ok(ApiResponse.Ok(data, TraceId));

    protected IActionResult Fail(string code, string message, int? status = null)
        => StatusCode(status ?? ErrorStatus.For(code), ApiResponse.Fail(code, message, TraceId));

    protected IActionResult Envelope<T>(Result<T> result)
        => result.IsSuccess ? Data(result.Value) : Problem(result);

    protected IActionResult Envelope(Result result)
        => result.IsSuccess ? Data(null) : Problem(result);

    protected IActionResult Problem(Result result)
    {
        var response = ApiResponse.FromResult(result, TraceId);
        return StatusCode(ErrorStatus.For(result.FirstError!.Code), response);
    }

    /// <summary>
    /// Server-side permission check for controllers that cannot use the host's filter
    /// attribute. Returns null when the caller is allowed.
    /// </summary>
    protected IActionResult? RequirePermission(string permission)
    {
        var tenant = Tenant;

        if (!tenant.IsResolved)
            return Fail(ErrorCode.Unauthorized, "Sign in to continue.");

        return tenant.Has(permission)
            ? null
            : Fail(ErrorCode.Forbidden, $"You do not have the '{permission}' permission.");
    }
}
