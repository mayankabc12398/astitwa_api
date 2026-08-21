using HrSuite.Common.Results;
using HrSuite.Extensions.Engine.Data;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.Extensions.Engine.Controllers;

/// <summary>
/// The Hook Log screen (section 10.6): read-only, tenant-scoped, filterable by status.
/// This is where a broken script shows up while the business screen carries on working
/// (acceptance scenario 4).
/// </summary>
[ApiController]
[Route("api/admin/hook-log")]
[Produces("application/json")]
public sealed class HookLogController : ExtensionControllerBase
{
    private readonly HookLogRepository _log;

    public HookLogController(HookLogRepository log) => _log = log;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] PageRequest page,
        [FromQuery] string? status,
        [FromQuery] int? hookId,
        CancellationToken ct)
        => Data(await _log.ListAsync(page, status, hookId, ct));
}
