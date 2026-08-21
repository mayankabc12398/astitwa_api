using HrSuite.Common.Results;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HrSuite.Extensions.Engine.Controllers;

/// <summary>
/// Every Layer 5 admin endpoint sits behind the admin.extensions permission, checked on the
/// server. Hiding the menu entry on the client is a courtesy, not the control.
/// </summary>
public abstract class ExtensionControllerBase : HrControllerBase, IAsyncActionFilter
{
    // [NonAction] matters: without it MVC treats this public method on an [ApiController]
    // as an action, and refuses to start because two of its parameters look like request bodies.
    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var denied = RequirePermission(Permissions.AdminExtensions);
        if (denied is not null)
        {
            context.Result = denied;
            return;
        }

        await next();
    }
}
