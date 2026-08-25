using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

/// <summary>
/// Recruitment requisitions.
///
/// The same five verbs every master screen has. What makes this one different is invisible
/// from here: the fields a hospital adds through the Screen Field Builder become columns on
/// hr_job_requisition, and travel in and out on the same payload as the shipped ones.
/// </summary>
[Route("api/hr/job-requisition")]
public sealed class JobRequisitionController : ApiControllerBase
{
    private readonly IJobRequisitionService _service;

    public JobRequisitionController(IJobRequisitionService service) => _service = service;

    [HttpGet]
    [RequirePermission(Permissions.JobRequisitionView)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(page, ct));

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.JobRequisitionView)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _service.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.JobRequisitionEdit)]
    public async Task<IActionResult> Save([FromBody] JobRequisition requisition, CancellationToken ct)
        => Envelope(await _service.SaveAsync(requisition, ct));

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.JobRequisitionEdit)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _service.DeleteAsync(id, ct));
}
