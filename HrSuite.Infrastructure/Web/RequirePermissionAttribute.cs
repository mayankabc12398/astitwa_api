using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace HrSuite.Infrastructure.Web;

/// <summary>
/// Permission check against the claims on the validated token. Server-side always
/// (section 11) — the client hides menu entries as a courtesy, not as a control.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _permission;

    public RequirePermissionAttribute(string permission) => _permission = permission;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();

        if (!tenant.IsResolved)
        {
            context.Result = new ObjectResult(ApiResponse.Fail(
                ErrorCode.Unauthorized, "Sign in to continue.", context.HttpContext.TraceIdentifier))
            { StatusCode = StatusCodes.Status401Unauthorized };
            return;
        }

        if (!tenant.Has(_permission))
        {
            context.Result = new ObjectResult(ApiResponse.Fail(
                ErrorCode.Forbidden, $"You do not have the '{_permission}' permission.", context.HttpContext.TraceIdentifier))
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
