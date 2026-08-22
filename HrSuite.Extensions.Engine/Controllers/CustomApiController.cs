using HrSuite.Extensions.Engine.Runtime;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.Extensions.Engine.Controllers;

/// <summary>
/// Where an endpoint an administrator wrote actually answers.
///
/// One route serves every one of them: the slug is data, looked up per request, so creating
/// an endpoint is an INSERT and nothing has to be built, deployed or restarted for it to
/// start answering. That is the whole feature.
///
/// Authentication is not declared here because the host's fallback policy already requires
/// it on every controller that does not opt out — an endpoint is never anonymous. Whether
/// the signed-in caller may run THIS one is the endpoint's own required_permission, checked
/// by the runner.
/// </summary>
[ApiController]
[Route("api/x/{slug}")]
[Produces("application/json")]
public sealed class CustomApiController : HrControllerBase
{
    private readonly CustomApiRunner _runner;

    public CustomApiController(CustomApiRunner runner) => _runner = runner;

    /// <summary>
    /// Parameters come from the query string. Everything arrives as text and is coerced to
    /// the declared type, so ?year=2026 and ?year="2026" behave the same.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(string slug, CancellationToken ct)
    {
        var supplied = Request.Query.ToDictionary(
            kv => kv.Key,
            kv => (object?)kv.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        return Data(await _runner.RunAsync(slug, supplied, ct));
    }

    /// <summary>
    /// Parameters come from the body, either bare — {"departmentId": 1} — or wrapped as
    /// {"params": {...}}. Both shapes turn up in the wild and neither is worth a support call.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Post(string slug, [FromBody] CustomApiCallRequest? request, CancellationToken ct)
    {
        var supplied = request?.Params ?? request?.Extra ?? new Dictionary<string, object?>();
        return Data(await _runner.RunAsync(slug, supplied, ct));
    }

    public sealed class CustomApiCallRequest
    {
        public Dictionary<string, object?>? Params { get; set; }

        /// <summary>
        /// Whatever else the body carried. A caller who posts the parameters at the top
        /// level rather than under "params" is understood rather than corrected.
        /// </summary>
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, object?>? Extra { get; set; }
    }
}
