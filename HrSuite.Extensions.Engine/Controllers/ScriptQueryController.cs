using HrSuite.Core.Abstractions;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.Extensions.Engine.Controllers;

/// <summary>
/// What api.query() calls from a CLIENT-side script.
///
/// The browser sandbox has no network access of its own — it posts a message to the host
/// page, which calls this endpoint with the user's own token. So a client script runs under
/// exactly the same permissions as a server script, checked here, and cannot see the token.
/// </summary>
[ApiController]
[Route("api/ext/query")]
[Produces("application/json")]
public sealed class ScriptQueryController : HrControllerBase
{
    private readonly INamedQueryRunner _runner;

    public ScriptQueryController(INamedQueryRunner runner) => _runner = runner;

    [HttpPost]
    public async Task<IActionResult> Run([FromBody] ScriptQueryRequest request, CancellationToken ct)
    {
        var result = await _runner.RunAsync(request.QueryKey, request.Params, ct);

        return Data(new
        {
            result.Ok,
            result.Rows,
            result.Columns,
            result.Truncated,
            result.Error
        });
    }

    public sealed class ScriptQueryRequest
    {
        public string QueryKey { get; set; } = string.Empty;
        public Dictionary<string, object?>? Params { get; set; }
    }
}
