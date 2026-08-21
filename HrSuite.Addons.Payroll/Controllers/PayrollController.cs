using HrSuite.Addons.Payroll.Data;
using HrSuite.Common.Results;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.Addons.Payroll.Controllers;

/// <summary>
/// Layer 3 endpoint.
///
/// [RequireModule] is the licence gate: a tenant whose sys_tenant_module row is off gets 403
/// here even though the assembly is deployed and the route exists (acceptance scenario 6).
/// The attribute is declared in base code, so the add-on depends downward and never the reverse.
/// </summary>
[ApiController]
[Route("api/payroll")]
[Produces("application/json")]
[RequireModule(PayrollModule.Key)]
[RequirePermission(PayrollPermissions.View)]
public sealed class PayrollController : HrControllerBase
{
    private readonly PayrollRunRepository _runs;

    public PayrollController(PayrollRunRepository runs) => _runs = runs;

    [HttpGet("runs")]
    public async Task<IActionResult> Runs([FromQuery] PageRequest page, CancellationToken ct)
        => Data(await _runs.ListAsync(page, ct));
}

/// <summary>An add-on declares its own permission keys; base code does not know them.</summary>
public static class PayrollPermissions
{
    public const string View = "payroll.view";
    public const string Run = "payroll.run";
}
