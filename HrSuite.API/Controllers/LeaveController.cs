using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

[Route("api/hr/leave")]
public sealed class LeaveController : ApiControllerBase
{
    private readonly ILeaveService _service;

    public LeaveController(ILeaveService service) => _service = service;

    [HttpGet]
    [RequirePermission(Permissions.LeaveView)]
    public async Task<IActionResult> List(
        [FromQuery] PageRequest page,
        [FromQuery] string? status,
        [FromQuery] int? employeeId,
        CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(page, status, employeeId, ct));

    /// <summary>The approval queue is the same list, pinned to pending.</summary>
    [HttpGet("pending")]
    [RequirePermission(Permissions.LeaveApprove)]
    public async Task<IActionResult> Pending([FromQuery] PageRequest page, CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(page, LeaveStatus.Pending, null, ct));

    [HttpGet("types")]
    [RequirePermission(Permissions.LeaveView)]
    public async Task<IActionResult> LeaveTypes(CancellationToken ct)
        => EnvelopeData(await _service.LeaveTypeLookupAsync(ct));

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.LeaveView)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _service.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.LeaveEdit)]
    public async Task<IActionResult> Save([FromBody] LeaveRequest request, CancellationToken ct)
        => Envelope(await _service.SaveAsync(request, ct));

    [HttpPost("decision")]
    [RequirePermission(Permissions.LeaveApprove)]
    public async Task<IActionResult> Decide([FromBody] LeaveDecision decision, CancellationToken ct)
        => Envelope(await _service.DecideAsync(decision, ct));
}
