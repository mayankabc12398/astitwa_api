using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Identity;
using HrSuite.Core.Extensibility;
using HrSuite.Extensions.Engine.Data;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Extensions.Engine.Runtime;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.Extensions.Engine.Controllers;

/// <summary>
/// The Script Hooks admin screen (section 10.6). Everything here is data maintenance —
/// no deployment, no build.
/// </summary>
[ApiController]
[Route("api/admin/hooks")]
[Produces("application/json")]
public sealed class ScriptHookController : ExtensionControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ScriptHookRepository _hooks;
    private readonly JintHookEngine _engine;
    private readonly ITenantContext _tenant;

    public ScriptHookController(ScriptHookRepository hooks, JintHookEngine engine, ITenantContext tenant)
    {
        _hooks = hooks;
        _engine = engine;
        _tenant = tenant;
    }

    /// <summary>
    /// The slots base code compiled in. The editor offers these rather than free text.
    ///
    /// Screens now carry their own slots and their own field list, so the editor can ask
    /// which page first and then offer only what that page actually has — including a
    /// concrete slot per field, rather than a hr.employee.field.&lt;fieldKey&gt;.onBlur
    /// placeholder the author has to hand-edit.
    ///
    /// HookKeys is still returned unchanged: it is what the free-text field falls back to,
    /// and a hook key outside this catalogue must keep working.
    /// </summary>
    [HttpGet("slots")]
    public IActionResult Slots() => Data(new
    {
        HookKeys = HookKeys.All,
        RunTargets = new[] { RunTarget.Client, RunTarget.Server },
        FieldSlotSuffix = ScreenCatalog.FieldSlotSuffix,
        Screens = ScreenCatalog.Screens.Select(screen => new
        {
            screen.Key,
            screen.Label,
            Slots = screen.Slots.Select(slot => new { slot.Key, slot.Label }),
            Fields = screen.Fields.Select(field => new
            {
                field.Key,
                field.Label,
                SlotKey = ScreenCatalog.FieldSlotKey(screen.Key, field.Key)
            })
        })
    });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageRequest page, [FromQuery] string? runOn, CancellationToken ct)
        => Data(await _hooks.ListAsync(page, runOn, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var hook = await _hooks.GetAsync(id, ct);
        return hook is null ? Fail(ErrorCode.NotFound, "Hook not found.", 404) : Data(hook);
    }

    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> History(int id, CancellationToken ct)
        => Data(await _hooks.HistoryAsync(id, ct));

    /// <summary>
    /// "Test with sample data". The admin screen keeps Save disabled until this returns ok,
    /// so a script that does not even parse cannot be activated.
    /// </summary>
    [HttpPost("test")]
    public IActionResult Test([FromBody] ScriptTestRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ScriptBody))
            return Fail(ErrorCode.Validation, "There is nothing to test.", 400);

        var form = ParseSample(request.SampleFormJson);

        var context = new HookContext
        {
            HookKey = request.HookKey,
            Form = form,
            User = new HookUser { Id = _tenant.UserId, Name = _tenant.UserName, Roles = _tenant.Roles.ToArray() },
            Tenant = new HookTenant { Id = _tenant.TenantId, Code = _tenant.TenantCode }
        };

        var outcome = _engine.Test(request.ScriptBody, context, ct);

        return Data(new ScriptTestResponse
        {
            Ok = outcome.Ok,
            Error = outcome.Error,
            DurationMs = outcome.DurationMs,
            Messages = outcome.Messages,
            Result = new
            {
                outcome.Result.CancelSave,
                outcome.Result.CancelNavigation,
                outcome.Result.RedirectTo,
                outcome.Result.Message,
                outcome.Result.Form
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] ScriptHookSaveRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.HookKey))
            return Fail(ErrorCode.Validation, "A hook key is required.", 400);

        if (!RunTarget.IsValid(request.RunOn))
            return Fail(ErrorCode.Validation, "run_on must be 'client' or 'server'.", 400);

        if (string.IsNullOrWhiteSpace(request.ScriptBody))
            return Fail(ErrorCode.Validation, "The script body is empty.", 400);

        // Activating a server script that does not even parse would be a foot-gun; refuse it.
        if (request.IsActive && request.RunOn == RunTarget.Server)
        {
            var probe = _engine.Test(request.ScriptBody, new HookContext { HookKey = request.HookKey }, ct);
            if (!probe.Ok && !probe.TimedOut)
                return Fail(ErrorCode.Validation, $"The script did not run cleanly: {probe.Error}", 400);
        }

        var canWriteGlobal = _tenant.Has(Permissions.AdminTenant);
        if (request.ApplyToAllTenants && !canWriteGlobal)
            return Fail(ErrorCode.Forbidden, "Writing a hook for every tenant needs the admin.tenant permission.", 403);

        var saved = await _hooks.SaveAsync(request, canWriteGlobal, ct);
        return saved is null ? Fail(ErrorCode.NotFound, "Hook not found.", 404) : Data(saved);
    }

    [HttpPost("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        var updated = await _hooks.SetActiveAsync(id, request.IsActive, ct);
        return updated is null ? Fail(ErrorCode.NotFound, "Hook not found.", 404) : Data(updated);
    }

    [HttpPost("{id:int}/rollback/{historyId:int}")]
    public async Task<IActionResult> Rollback(int id, int historyId, CancellationToken ct)
    {
        var restored = await _hooks.RollbackAsync(id, historyId, ct);
        return restored is null ? Fail(ErrorCode.NotFound, "That version is no longer available.", 404) : Data(restored);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _hooks.DeleteAsync(id, ct);
        return Data(null);
    }

    private static Dictionary<string, object?> ParseSample(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, Json);
            return parsed is null
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class SetActiveRequest
    {
        public bool IsActive { get; set; }
    }
}
