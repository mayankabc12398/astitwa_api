using System.Diagnostics;
using System.Text;
using HrSuite.Common.Results;
using HrSuite.Extensions.Engine.Data;
using HrSuite.Extensions.Engine.Models;
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
    private readonly AiThreadRepository _threads;

    public AiAssistController(VertexAiClient vertex, AiThreadRepository threads)
    {
        _vertex = vertex;
        _threads = threads;
    }

    /// <summary>Lets the editor hide its assistant rather than offer a button that always fails.</summary>
    [HttpGet("status")]
    public IActionResult Status() => Data(new
    {
        Available = _vertex.IsConfigured,
        Model = _vertex.IsConfigured ? _vertex.Model : null
    });

    /// <summary>
    /// The conversation about one hook or one endpoint, as it was left.
    ///
    /// A read never creates a row: opening an editor is not the same as having asked
    /// something, and a table full of empty threads would say it was.
    /// </summary>
    [HttpGet("thread")]
    public async Task<IActionResult> Thread([FromQuery] string? key, [FromQuery] int limit, CancellationToken ct)
    {
        if (!IsStorable(key)) return Fail(ErrorCode.Validation, "A conversation needs a key.");

        var messages = await _threads.MessagesAsync(key!, limit is > 0 and <= 200 ? limit : 100, ct);
        return Data(new { ThreadKey = key, Messages = messages });
    }

    /// <summary>Every conversation this user has had, most recently used first.</summary>
    [HttpGet("threads")]
    public async Task<IActionResult> Threads([FromQuery] PageRequest page, CancellationToken ct)
        => Data(await _threads.ListAsync(page, ct));

    /// <summary>
    /// "Clear this conversation" — a real delete, because that is what the words promise.
    /// The procedure filters on the signed-in user, so it can only reach their own thread.
    /// </summary>
    [HttpDelete("thread")]
    public async Task<IActionResult> ClearThread([FromQuery] string? key, CancellationToken ct)
    {
        if (!IsStorable(key)) return Fail(ErrorCode.Validation, "A conversation needs a key.");

        await _threads.ClearAsync(key!, ct);
        return Data(new { Cleared = true });
    }

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
            var clock = Stopwatch.StartNew();
            var answer = await _vertex.GenerateAsync(SystemPrompt(request), turns, ct);
            clock.Stop();

            await RememberAsync(request, answer, clock.ElapsedMilliseconds);
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
    /// Narration audio for the walkthrough videos, in whichever voice the caller names.
    ///
    /// Behind the same permission as the rest of this controller, and returning audio rather
    /// than a token: a build script on somebody's laptop gets its MP3 without ever holding a
    /// Google credential of its own.
    /// </summary>
    [HttpPost("speak")]
    public async Task<IActionResult> Speak([FromBody] SpeakRequest request, CancellationToken ct)
    {
        if (!_vertex.IsConfigured)
            return Fail(ErrorCode.Validation, "Vertex AI is not configured on this server.");

        if (string.IsNullOrWhiteSpace(request.Text))
            return Fail(ErrorCode.Validation, "There is nothing to say.");

        if (request.Text.Length > 5000)
            return Fail(ErrorCode.Validation, "Text-to-Speech takes 5000 characters at a time. Split the scene.");

        try
        {
            var audio = await _vertex.SpeakAsync(
                request.Text,
                string.IsNullOrWhiteSpace(request.Voice) ? "en-IN-Wavenet-D" : request.Voice,
                string.IsNullOrWhiteSpace(request.LanguageCode) ? "en-IN" : request.LanguageCode,
                request.SpeakingRate is < 0.25 or > 4 ? 1.0 : request.SpeakingRate,
                ct);

            return File(audio, "audio/mpeg");
        }
        catch (Exception cause)
        {
            return Fail(ErrorCode.Unexpected, cause.Message);
        }
    }

    public sealed class SpeakRequest
    {
        public string Text { get; set; } = string.Empty;
        /// <summary>An en-IN voice, e.g. en-IN-Wavenet-D (male) or en-IN-Wavenet-A (female).</summary>
        public string? Voice { get; set; }
        public string? LanguageCode { get; set; }
        public double SpeakingRate { get; set; } = 0.95;
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

        // Kept as it streams, so the thread can be filed afterwards. The browser shows the
        // answer as it arrives; the database gets it once, whole.
        var written = new StringBuilder();
        var clock = Stopwatch.StartNew();

        try
        {
            await foreach (var chunk in _vertex.StreamAsync(SystemPrompt(request), turns, ct))
            {
                written.Append(chunk);
                await SendAsync("chunk", chunk, ct);
            }

            await SendAsync("done", _vertex.Model, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The reader closed the connection — pressed Stop, asked again, or left the
            // screen. Whatever had arrived by then is still an answer the panel is showing,
            // so it is filed rather than dropped.
        }
        catch (Exception cause)
        {
            await SendAsync("error", cause.Message, ct);
        }

        clock.Stop();
        await RememberAsync(request, written.ToString(), clock.ElapsedMilliseconds);
    }

    /// <summary>
    /// Files the exchange against its thread.
    ///
    /// The question is stored, not the editor buffer that went with it: the buffer is
    /// already in the hook's own version history, and storing it per question would put the
    /// same script in the database twenty times over.
    ///
    /// Never allowed to fail the answer. The user asked a question and got one; a
    /// conversation that could not be written down is not their problem, and throwing here
    /// would turn a working assistant into a broken one.
    /// </summary>
    private async Task RememberAsync(AssistRequest request, string answer, long elapsedMs)
    {
        if (!IsStorable(request.ThreadKey)) return;

        var key = request.ThreadKey!.Trim();
        var title = string.IsNullOrWhiteSpace(request.Title) ? request.Context : request.Title;

        try
        {
            // CancellationToken.None on purpose: a cancelled request is the commonest way
            // this is reached, and passing the cancelled token would skip the write.
            await _threads.AddAsync(
                key, title, request.Language, AiMessageRole.User, request.Question,
                null, null, CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(answer))
            {
                await _threads.AddAsync(
                    key, title, request.Language, AiMessageRole.Model, answer,
                    _vertex.Model, (int)Math.Min(elapsedMs, int.MaxValue), CancellationToken.None);
            }
        }
        catch
        {
            // Deliberately swallowed. The hook log is for scripts; this is a note-keeping
            // side effect, and the answer has already been delivered.
        }
    }

    /// <summary>A key that is missing, blank or longer than the column is not stored.</summary>
    private static bool IsStorable(string? threadKey)
        => !string.IsNullOrWhiteSpace(threadKey) && threadKey.Trim().Length <= 160;

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
            "You help an administrator of Demo Hospital, a multi-tenant hospital product, inside its own code editor. " +
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
        /// <summary>
        /// Which conversation this belongs to, e.g. hook:hr.patient.onLoad. Absent means
        /// "do not file this" — the panel still works, it just forgets.
        /// </summary>
        public string? ThreadKey { get; set; }
        /// <summary>What to call the thread in a list. Falls back to Context.</summary>
        public string? Title { get; set; }
        public List<AssistTurn>? History { get; set; }
    }

    public sealed class AssistTurn
    {
        public string Role { get; set; } = "user";
        public string Text { get; set; } = string.Empty;
    }
}
