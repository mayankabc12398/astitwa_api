using HrSuite.Common.Results;
using HrSuite.Core.Modularity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace HrSuite.Infrastructure.Web;

/// <summary>
/// Licensing gate for Layer 3 (section 8, acceptance scenario 6). An add-on controller decorates
/// itself with its own module key; the check lives here in base code and reads sys_tenant_module.
/// The attribute is declared in the host, so an add-on depends downward — never the reverse.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireModuleAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _moduleKey;

    public RequireModuleAttribute(string moduleKey) => _moduleKey = moduleKey;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var modules = context.HttpContext.RequestServices.GetRequiredService<ITenantModuleService>();

        if (!await modules.IsEnabledAsync(_moduleKey, context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(ApiResponse.Fail(
                ErrorCode.ModuleDisabled,
                $"The '{_moduleKey}' module is not enabled for this tenant.",
                context.HttpContext.TraceIdentifier))
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
