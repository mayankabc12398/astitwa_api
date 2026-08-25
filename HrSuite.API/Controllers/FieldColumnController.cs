using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

/// <summary>
/// The Screen Field Builder that writes real columns.
///
/// Every write here is a schema change, so every write is behind admin.fieldColumn — a
/// permission of its own rather than the one that guards the row-based builder. The layout
/// read is not: every form that draws these fields calls it, and a form that cannot read its
/// own shape cannot render.
/// </summary>
[Route("api/hr/field-column")]
public sealed class FieldColumnController : ApiControllerBase
{
    private readonly IFieldColumnService _service;

    public FieldColumnController(IFieldColumnService service) => _service = service;

    [HttpGet("screen")]
    [RequirePermission(Permissions.AdminFieldColumn)]
    public async Task<IActionResult> Screens(CancellationToken ct)
        => EnvelopeData(await _service.ScreensAsync(ct));

    [HttpGet("control-type")]
    [RequirePermission(Permissions.AdminFieldColumn)]
    public IActionResult ControlTypes()
        => EnvelopeData(_service.ControlTypes());

    /// <summary>Deliberately not admin-only: the runtime form reads its own layout through this.</summary>
    [HttpGet("screen/{screenCode}/layout")]
    public async Task<IActionResult> Layout(string screenCode, CancellationToken ct)
        => Envelope(await _service.LayoutAsync(screenCode, ct));

    [HttpPost("screen/{screenCode}/field")]
    [RequirePermission(Permissions.AdminFieldColumn)]
    public async Task<IActionResult> Save(string screenCode, [FromBody] FieldColumn field, CancellationToken ct)
        => Envelope(await _service.SaveAsync(screenCode, field, ct));

    /// <summary>
    /// The column name is confirmed in the query string on purpose: a delete that needs only
    /// an id is one stale browser tab away from dropping the wrong column.
    /// </summary>
    [HttpDelete("field/{fieldId:int}")]
    [RequirePermission(Permissions.AdminFieldColumn)]
    public async Task<IActionResult> Delete(int fieldId, [FromQuery] string column, CancellationToken ct)
        => Envelope(await _service.DeleteAsync(fieldId, column ?? string.Empty, ct));

    [HttpPost("reorder")]
    [RequirePermission(Permissions.AdminFieldColumn)]
    public async Task<IActionResult> Reorder([FromBody] List<FieldColumnPosition> items, CancellationToken ct)
        => Envelope(await _service.ReorderAsync(items ?? new List<FieldColumnPosition>(), ct));

    [HttpGet("audit")]
    [RequirePermission(Permissions.AdminFieldColumn)]
    public async Task<IActionResult> Audit([FromQuery] string? screenCode, CancellationToken ct)
        => EnvelopeData(await _service.AuditAsync(screenCode, ct));
}
