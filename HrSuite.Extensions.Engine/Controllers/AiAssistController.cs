using System.Text;
using HrSuite.Common.Results;
using HrSuite.Extensions.Engine.Runtime;
// WriteAsync is an extension on HttpResponse, and the streaming endpoint writes its events
// straight to the body rather than returning an IActionResult.
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HrSuite.Extensions.Engine.Controllers;

/// <summary>
/// The assistant beside the script and SQL editors.
///
/// It sits behind admin.extensions like every other Layer 5 surface, because the thing it
/// helps you write — a hook script, or an endpoint's SELECT — is only editable by someone
/// who already holds that permission. Anyone who could reach this could reach the editor.
///
/// The browser never holds the Google credential. It posts the editor's contents and a
/// question here; this controller asks Vertex and returns the answer as text.
/// </summary>
[ApiController]
[Route("api/ai")]
[Produces("application/json")]
public sealed class AiAssistController : ExtensionControllerBase
{
    private readonly VertexAiClient _vertex;

    public AiAssistController(VertexAiClient vertex) => _vertex = vertex;

    /// <summary>Lets the editor hide its assistant rather than offer a button that always fails.</summary>
    [HttpGet("status")]
    public IActionResult Status() => Data(new
    {
        Available = _vertex.IsConfigured,
        Model = _vertex.IsConfigured ? _vertex.Model : null
    });

    [HttpPost("assist")]
    public async Task<IActionResult> Assist([FromBody] AssistRequest request, CancellationToken ct)
    {
        if (!_vertex.IsConfigured)
            return Fail(ErrorCode.Validation, "Vertex AI is not configured on this server.");

        if (string.IsNullOrWhiteSpace(request.Question))
            return Fail(ErrorCode.Validation, "Ask something.");

        // A whole editor buffer is fine; a pasted database is not. The cap is here rather
        // than in the browser because the browser is where a caller would remove it.
        if ((request.Code?.Length ?? 0) > 60000)
            return Fail(ErrorCode.Validation, "The editor contents are too long to send.");

        var turns = new List<(string Role, string Text)>();

        // Earlier turns first, so a follow-up like "now make it handle no rows" has the
        // answer it is following up on.
        foreach (var turn in request.History ?? new List<AssistTurn>())
        {
            if (string.IsNullOrWhiteSpace(turn.Text)) continue;
            turns.Add((turn.Role == "model" ? "model" : "user", turn.Text));
        }

        turns.Add(("user", UserMessage(request)));

        try
        {
            var answer = await _vertex.GenerateAsync(SystemPrompt(request), turns, ct);
            return Data(new { Answer = answer, Model = _vertex.Model });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The user navigated away or asked again. Not a failure worth reporting.
            return Data(new { Answer = string.Empty, Model = _vertex.Model });
        }
        catch (Exception cause)
        {
            return Fail(ErrorCode.Unexpected, cause.Message);
        }
    }

    /// <summary>
    /// The same answer, sent as it is written.
    ///
    /// Server-sent events rather than a WebSocket: this is one-way, short-lived text, and SSE
    /// costs a content type. It is a POST, so the browser reads it with fetch rather than
    /// EventSource — which cannot carry an Authorization header, and this endpoint is behind
    /// the same permission as everything else here.
    /// </summary>
    [HttpPost("assist/stream")]
    public async Task Stream([FromBody] AssistRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        // nginx and IIS buffer a response by default, which holds every chunk until the
        // answer is finished — the exact thing this endpoint exists to avoid.
        Response.Headers["X-Accel-Buffering"] = "no";

        // Kestrel buffers too, and a flush that only reaches the buffer is not a flush.
        HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

        if (!_vertex.IsConfigured)
        {
            await SendAsync("error", "Vertex AI is not configured on this server.", ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await SendAsync("error", "Ask something.", ct);
            return;
        }

        if ((request.Code?.Length ?? 0) > 60000)
        {
            await SendAsync("error", "The editor contents are too long to send.", ct);
            return;
        }

        var turns = new List<(string Role, string Text)>();
        foreach (var turn in request.History ?? new List<AssistTurn>())
        {
            if (string.IsNullOrWhiteSpace(turn.Text)) continue;
            turns.Add((turn.Role == "model" ? "model" : "user", turn.Text));
        }
        turns.Add(("user", UserMessage(request)));

        try
        {
            await foreach (var chunk in _vertex.StreamAsync(SystemPrompt(request), turns, ct))
            {
                await SendAsync("chunk", chunk, ct);
            }

            await SendAsync("done", _vertex.Model, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The reader closed the connection — asked again, or left the screen. Nothing to say.
        }
        catch (Exception cause)
        {
            await SendAsync("error", cause.Message, ct);
        }
    }

    /// <summary>
    /// One SSE event. The text is JSON-encoded rather than written raw because a newline in
    /// the payload would otherwise end the event halfway through a line of code.
    /// </summary>
    private async Task SendAsync(string type, string text, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { type, text });
        await Response.WriteAsync($"data: {payload}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// What the model is told about where it is.
    ///
    /// Without this it writes generic JavaScript that reaches for fetch, or generic MySQL
    /// with no tenant filter — both of which this product refuses to run. Describing the
    /// contract up front is cheaper than correcting every answer.
    /// </summary>
    private static string SystemPrompt(AssistRequest request)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You help an administrator of HrSuite, a multi-tenant HR product, inside its own code editor. " +
            "Answer briefly. When you write code, give one complete block that can be pasted in as is, " +
            "and explain only what is not obvious from reading it.");

        if (request.Language == "mysql")
        {
            builder.AppendLine();
            builder.AppendLine("The editor holds ONE MySQL SELECT for an API Builder endpoint. The rules are enforced by the server and a statement that breaks any of them cannot be saved:");
            builder.AppendLine("- Exactly one statement, starting with SELECT or WITH. No semicolon, no comments (--, #, /* */).");
            builder.AppendLine("- No INSERT/UPDATE/DELETE/DROP/ALTER/CREATE/CALL/SET, no INTO OUTFILE, no information_schema, mysql. or sys.");
            builder.AppendLine("- It MUST contain the literal token {tenant} where the tenant id belongs, e.g. WHERE e.tenant_id = {tenant}. The server replaces it with a bound parameter.");
            builder.AppendLine("- Parameters are written @name and must match the endpoint's declared parameters exactly.");
            builder.AppendLine("- Every returned column needs a unique name; alias duplicates (d.dept_name AS department_name).");
            builder.AppendLine();
            builder.AppendLine("Tables include hr_employee (employee_id, tenant_id, employee_code, full_name, dob, date_of_joining, department_id, designation_id, reporting_manager_id, mobile, email, employment_status, gross_ctc, hra, tds, net_salary, is_active), hr_department (department_id, tenant_id, dept_code, dept_name, is_active), hr_designation (designation_id, tenant_id, desig_name, is_active), hr_leave_request and hr_leave_type. Rows are soft-deleted with is_active = 0.");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("The editor holds ONE client hook script. It is compiled as the body of an async function and runs in a sandboxed iframe with no DOM, no fetch and no storage. Only these four are in scope:");
            builder.AppendLine("- ctx: { hookKey, form, custom, value, response, user, tenant, setForm(), setCustom() }. form holds the product's own fields; custom holds fields the tenant added.");
            builder.AppendLine("- api: query(queryKey, params) for a registered named query, and callEndpoint(slug, params) for an API Builder endpoint. Both resolve to { ok, rows, columns, truncated, error }. Row keys are the SQL column names, usually snake_case.");
            builder.AppendLine("- ui: toast(), error(), confirm(), pickList({ title, columns, rows, emptyAction }), openScreen(route). The script supplies data; the product draws.");
            builder.AppendLine("- utils: age(), formatDate(), round(), isEmpty().");
            builder.AppendLine();
            builder.AppendLine("Return a value to act on the screen: { form: {...} } writes fields, { custom: {...} } writes tenant fields, { readOnly: ['field'] } locks them, { message: '...' } toasts, { cancelSave: true } stops a save in beforeSave. Use await freely. Never use fetch, window, document or setTimeout.");
        }

        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            builder.AppendLine();
            builder.AppendLine("Where this runs: " + request.Context);
        }

        return builder.ToString();
    }

    private static string UserMessage(AssistRequest request)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(request.Selection))
        {
            builder.AppendLine("The selected part of the editor:");
            builder.AppendLine("```");
            builder.AppendLine(request.Selection);
            builder.AppendLine("```");
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            builder.AppendLine(string.IsNullOrWhiteSpace(request.Selection)
                ? "The editor currently holds:"
                : "The whole editor, for context:");
            builder.AppendLine("```");
            builder.AppendLine(request.Code);
            builder.AppendLine("```");
            builder.AppendLine();
        }

        builder.Append(request.Question);
        return builder.ToString();
    }

    public sealed class AssistRequest
    {
        /// <summary>mysql | javascript. Decides which contract the model is told about.</summary>
        public string Language { get; set; } = "javascript";
        public string? Code { get; set; }
        public string? Selection { get; set; }
        public string Question { get; set; } = string.Empty;
        /// <summary>Which hook slot or endpoint this is, when the screen knows.</summary>
        public string? Context { get; set; }
        public List<AssistTurn>? History { get; set; }
    }

    public sealed class AssistTurn
    {
        public string Role { get; set; } = "user";
        public string Text { get; set; } = string.Empty;
    }
}
