using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

[Route("api/hr/department")]
public sealed class DepartmentController : ApiControllerBase
{
    private readonly IDepartmentService _service;

    public DepartmentController(IDepartmentService service) => _service = service;

    [HttpGet]
    [RequirePermission(Permissions.DepartmentView)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(page, ct));

    [HttpGet("lookup")]
    [RequirePermission(Permissions.DepartmentView)]
    public async Task<IActionResult> Lookup(CancellationToken ct)
        => EnvelopeData(await _service.LookupAsync(ct));

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.DepartmentView)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _service.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.DepartmentEdit)]
    public async Task<IActionResult> Save([FromBody] Department department, CancellationToken ct)
        => Envelope(await _service.SaveAsync(department, ct));

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.DepartmentEdit)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _service.DeleteAsync(id, ct));
}
