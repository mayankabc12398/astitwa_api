using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

/// <summary>
/// Employee documents — offer, appointment, confirmation and the rest.
///
/// Issuing is a separate permission from editing: preparing a draft and putting a letter in
/// somebody's hand are different acts, and plenty of tenants want the second one held by
/// fewer people than the first.
/// </summary>
[Route("api/hr/document")]
public sealed class DocumentController : ApiControllerBase
{
    private readonly IDocumentService _service;

    public DocumentController(IDocumentService service) => _service = service;

    [HttpGet]
    [RequirePermission(Permissions.DocumentView)]
    public async Task<IActionResult> List(
        [FromQuery] PageRequest page,
        [FromQuery] string? status,
        [FromQuery] int? employeeId,
        CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(page, status, employeeId, ct));

    /// <summary>
    /// Counts across the whole register. The list is paged and clamped, so the headline
    /// figures and the gallery counts cannot be totalled from what a page happens to carry.
    /// </summary>
    [HttpGet("stats")]
    [RequirePermission(Permissions.DocumentView)]
    public async Task<IActionResult> Stats(CancellationToken ct)
        => EnvelopeData(await _service.StatsAsync(ct));

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.DocumentView)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _service.GetAsync(id, ct));

    /// <summary>Everything the client renderer needs to lay this document out.</summary>
    [HttpGet("{id:int}/print")]
    [RequirePermission(Permissions.DocumentView)]
    public async Task<IActionResult> PrintContext(int id, CancellationToken ct)
        => Envelope(await _service.PrintContextAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.DocumentEdit)]
    public async Task<IActionResult> Save([FromBody] Document document, CancellationToken ct)
        => Envelope(await _service.SaveAsync(document, ct));

    [HttpPost("{id:int}/status")]
    [RequirePermission(Permissions.DocumentIssue)]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] DocumentStatusChange change, CancellationToken ct)
    {
        change.DocumentId = id;
        return Envelope(await _service.ChangeStatusAsync(change, ct));
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.DocumentEdit)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _service.DeleteAsync(id, ct));
}
