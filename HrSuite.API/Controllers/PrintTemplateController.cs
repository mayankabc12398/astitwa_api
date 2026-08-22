using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

/// <summary>
/// The print designer. Editing a template is an administrative act, so the write side is
/// gated on admin.printTemplate; resolving one is not, because every screen that prints
/// needs it and a document already carries its own permission.
/// </summary>
[Route("api/hr/print-template")]
public sealed class PrintTemplateController : ApiControllerBase
{
    private readonly IPrintTemplateService _service;

    public PrintTemplateController(IPrintTemplateService service) => _service = service;

    [HttpGet("document-type")]
    [RequirePermission(Permissions.DocumentView)]
    public async Task<IActionResult> DocumentTypes(CancellationToken ct)
        => EnvelopeData(await _service.DocumentTypesAsync(ct));

    [HttpGet]
    [RequirePermission(Permissions.AdminPrintTemplate)]
    public async Task<IActionResult> List([FromQuery] PageRequest page, [FromQuery] string? documentType, CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(page, documentType, ct));

    [HttpGet("lookup")]
    [RequirePermission(Permissions.DocumentView)]
    public async Task<IActionResult> Lookup([FromQuery] string? documentType, CancellationToken ct)
        => EnvelopeData(await _service.LookupAsync(documentType, ct));

    /// <summary>
    /// The template the runtime should print with. A 404 here is the documented answer for
    /// "this tenant has configured none", and the client falls back to its built-in layout.
    /// </summary>
    [HttpGet("resolve")]
    [RequirePermission(Permissions.DocumentView)]
    public async Task<IActionResult> Resolve([FromQuery] string documentType, CancellationToken ct)
        => Envelope(await _service.ResolveAsync(documentType, ct));

    [HttpGet("available-field")]
    [RequirePermission(Permissions.AdminPrintTemplate)]
    public async Task<IActionResult> AvailableFields([FromQuery] string documentType, CancellationToken ct)
        => EnvelopeData(await _service.AvailableFieldsAsync(documentType, ct));

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.AdminPrintTemplate)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _service.GetAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.AdminPrintTemplate)]
    public async Task<IActionResult> Save([FromBody] PrintTemplate template, CancellationToken ct)
        => Envelope(await _service.SaveAsync(template, ct));

    [HttpPost("{id:int}/clone")]
    [RequirePermission(Permissions.AdminPrintTemplate)]
    public async Task<IActionResult> Clone(int id, [FromBody] PrintTemplateCloneRequest request, CancellationToken ct)
    {
        // The route carries the identity; the body only names the copy.
        request.TemplateId = id;
        return Envelope(await _service.CloneAsync(request, ct));
    }

    [HttpPost("{id:int}/default")]
    [RequirePermission(Permissions.AdminPrintTemplate)]
    public async Task<IActionResult> SetDefault(int id, CancellationToken ct)
        => Envelope(await _service.SetDefaultAsync(id, ct));

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.AdminPrintTemplate)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _service.DeleteAsync(id, ct));
}
