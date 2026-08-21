using HrSuite.Extensions.Engine.Data;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.Extensions.Engine.Controllers;

/// <summary>
/// Where the browser sandbox reports a client-side script failure.
///
/// Deliberately NOT behind the admin.extensions permission: the person whose screen just ran
/// a broken script is usually an ordinary HR user, and their failure is exactly the one the
/// hook log needs to capture (acceptance scenario 4). Reading the log still needs the admin
/// permission — see <see cref="HookLogController"/>.
///
/// The tenant is stamped by the repository base, so a caller cannot log against another
/// tenant, and the payload is length-capped before it is stored.
/// </summary>
[ApiController]
[Route("api/ext/hook-log")]
[Produces("application/json")]
public sealed class ClientHookLogController : HrControllerBase
{
    private readonly HookLogRepository _log;

    public ClientHookLogController(HookLogRepository log) => _log = log;

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] ClientLogRequest request, CancellationToken ct)
    {
        var status = request.Status is HookLogStatus.Ok or HookLogStatus.Error or HookLogStatus.Timeout
            ? request.Status
            : HookLogStatus.Error;

        await _log.WriteAsync(
            request.HookId,
            request.HookKey ?? string.Empty,
            RunTarget.Client,
            status,
            request.DurationMs,
            request.Message,
            request.ContextJson,
            ct);

        return Data(null);
    }

    public sealed class ClientLogRequest
    {
        public int? HookId { get; set; }
        public string? HookKey { get; set; }
        public string Status { get; set; } = HookLogStatus.Error;
        public int DurationMs { get; set; }
        public string? Message { get; set; }
        public string? ContextJson { get; set; }
    }
}
