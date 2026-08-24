using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

[Route("api/hr/patient")]
public sealed class PatientController : ApiControllerBase
{
    private readonly IPatientService _service;

    public PatientController(IPatientService service) => _service = service;

    [HttpGet]
    [RequirePermission(Permissions.PatientView)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(page, ct));

    [HttpGet("lookup")]
    [RequirePermission(Permissions.PatientView)]
    public async Task<IActionResult> Lookup(CancellationToken ct)
        => EnvelopeData(await _service.LookupAsync(ct));

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.PatientView)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _service.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.PatientEdit)]
    public async Task<IActionResult> Save([FromBody] Patient patient, CancellationToken ct)
        => Envelope(await _service.SaveAsync(patient, ct));

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.PatientEdit)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _service.DeleteAsync(id, ct));
}
