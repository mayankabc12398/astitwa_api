using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

[Route("api/hr/designation")]
public sealed class DesignationController : ApiControllerBase
{
    private readonly IDesignationService _service;

    public DesignationController(IDesignationService service) => _service = service;

    [HttpGet]
    [RequirePermission(Permissions.DesignationView)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(page, ct));

    [HttpGet("lookup")]
    [RequirePermission(Permissions.DesignationView)]
    public async Task<IActionResult> Lookup(CancellationToken ct)
        => EnvelopeData(await _service.LookupAsync(ct));

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.DesignationView)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _service.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.DesignationEdit)]
    public async Task<IActionResult> Save([FromBody] Designation designation, CancellationToken ct)
        => Envelope(await _service.SaveAsync(designation, ct));

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.DesignationEdit)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _service.DeleteAsync(id, ct));
}
