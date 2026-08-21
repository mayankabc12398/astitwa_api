using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

[Route("api/hr/employee")]
public sealed class EmployeeController : ApiControllerBase
{
    private readonly IEmployeeService _service;

    public EmployeeController(IEmployeeService service) => _service = service;

    [HttpGet]
    [RequirePermission(Permissions.EmployeeView)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(page, ct));

    [HttpGet("lookup")]
    [RequirePermission(Permissions.EmployeeView)]
    public async Task<IActionResult> Lookup(CancellationToken ct)
        => EnvelopeData(await _service.LookupAsync(ct));

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.EmployeeView)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _service.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.EmployeeEdit)]
    public async Task<IActionResult> Save([FromBody] Employee employee, CancellationToken ct)
        => Envelope(await _service.SaveAsync(employee, ct));

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.EmployeeEdit)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _service.DeleteAsync(id, ct));
}
