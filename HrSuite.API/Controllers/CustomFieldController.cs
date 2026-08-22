using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.API.Controllers;

/// <summary>
/// The field builder. Defining a field is administrative; reading the definitions and
/// writing a record's values are not, because every form that renders a custom field has to
/// do both and is already gated on its own screen's permission.
/// </summary>
[Route("api/hr/custom-field")]
public sealed class CustomFieldController : ApiControllerBase
{
    private readonly ICustomFieldService _service;

    public CustomFieldController(ICustomFieldService service) => _service = service;

    [HttpGet("screen")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> Screens(CancellationToken ct)
        => EnvelopeData(await _service.ScreensAsync(ct));

    [HttpGet("control-type")]
    [RequirePermission(Permissions.AdminCustomField)]
    public IActionResult ControlTypes()
        => EnvelopeData(_service.ControlTypes());

    /// <summary>
    /// Deliberately not an admin-only read: every form that renders custom fields calls
    /// this, and hiding the definitions would leave those forms unable to draw themselves.
    /// The list carries no values, only shape.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string screenKey, CancellationToken ct)
        => EnvelopeData(await _service.ListAsync(screenKey, ct));

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _service.GetAsync(id, ct));

    [HttpGet("{id:int}/usage")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> Usage(int id, CancellationToken ct)
        => EnvelopeData(await _service.UsageAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> Save([FromBody] CustomField field, CancellationToken ct)
        => Envelope(await _service.SaveAsync(field, ct));

    [HttpPost("reorder")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> Reorder([FromBody] List<CustomFieldOrderEntry> items, CancellationToken ct)
        => Envelope(await _service.ReorderAsync(items, ct));

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _service.DeleteAsync(id, ct));

    // ---- computed fields -------------------------------------------------

    /// <summary>
    /// Validates a formula and runs it over sample values. A rejected formula comes back as
    /// a successful call carrying isValid=false — it is the author's answer, not a fault,
    /// and the editor shows the message beside the formula box.
    /// </summary>
    [HttpPost("test-formula")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> TestFormula([FromBody] FormulaTestRequest request, CancellationToken ct)
        => Envelope(await _service.TestFormulaAsync(request, ct));

    // ---- bound dropdowns -------------------------------------------------

    [HttpGet("data-source")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> DataSources(CancellationToken ct)
        => EnvelopeData(await _service.DataSourcesAsync(ct));

    [HttpPost("probe")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> Probe([FromBody] SourceProbeRequest request, CancellationToken ct)
        => Envelope(await _service.ProbeAsync(request, ct));

    /// <summary>
    /// One field's options. Not admin-gated for the same reason the definitions are not:
    /// every form rendering a bound dropdown has to resolve it, and the values behind it
    /// are already bounded by whatever the source itself allows.
    /// </summary>
    [HttpGet("{id:int}/options")]
    public async Task<IActionResult> Options(
        int id, [FromQuery] string? search, [FromQuery] string? parentValue, CancellationToken ct)
        => Envelope(await _service.OptionsAsync(id, search, parentValue, ct));

    // ---- audit and archive -----------------------------------------------

    [HttpGet("audit")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> Audit([FromQuery] PageRequest page, [FromQuery] string? screenKey, CancellationToken ct)
        => EnvelopeData(await _service.AuditAsync(page, screenKey, ct));

    [HttpGet("archive")]
    [RequirePermission(Permissions.AdminCustomField)]
    public async Task<IActionResult> Archive([FromQuery] PageRequest page, [FromQuery] int? fieldId, CancellationToken ct)
        => EnvelopeData(await _service.ArchiveAsync(page, fieldId, ct));

    // ---- values ----------------------------------------------------------

    [HttpGet("value")]
    public async Task<IActionResult> Values([FromQuery] string screenKey, [FromQuery] int recordId, CancellationToken ct)
        => EnvelopeData(await _service.ValuesAsync(screenKey, recordId, ct));

    /// <summary>
    /// Written after the record's own save has returned an id. The screen's edit permission
    /// is what governs this — a user who may not edit an employee may not fill an
    /// employee's extra fields either.
    /// </summary>
    [HttpPost("value")]
    public async Task<IActionResult> SaveValues([FromBody] CustomValueSaveRequest request, CancellationToken ct)
    {
        var denied = RequirePermissionFor(request.ScreenKey);
        if (denied is not null) return denied;

        return Envelope(await _service.SaveValuesAsync(request, ct));
    }

    /// <summary>
    /// Maps a screen key to the permission that already governs writing to that screen, so
    /// custom values can never be a way around a screen's own gate. An unknown screen falls
    /// back to the custom-field admin permission rather than to nothing.
    /// </summary>
    private IActionResult? RequirePermissionFor(string? screenKey) => screenKey switch
    {
        "hr.employee" => RequirePermission(Permissions.EmployeeEdit),
        "hr.leaveRequest" => RequirePermission(Permissions.LeaveEdit),
        _ => RequirePermission(Permissions.AdminCustomField)
    };
}
