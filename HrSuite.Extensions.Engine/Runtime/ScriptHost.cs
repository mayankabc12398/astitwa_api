using System.Diagnostics;
using System.Text.Json;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Extensibility;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace HrSuite.Extensions.Engine.Runtime;

/// <summary>
/// One sandboxed evaluation of one script body (section 10.5).
///
/// The engine is created per run with a three-second wall clock, a four-megabyte memory
/// ceiling, strict mode and a recursion cap. CLR interop is never enabled, so a script
/// cannot reach a .NET type, the file system or the network — the only doors out are the
/// four objects handed in.
/// </summary>
public sealed class ScriptHost
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    public const long MemoryLimitBytes = 4 * 1024 * 1024;

    private readonly INamedQueryRunner _queries;
    private readonly ICustomApiCaller? _endpoints;

    /// <param name="endpoints">
    /// Optional so a host can be built without the API Builder — the sandbox tests do. With
    /// none supplied, <c>api.callEndpoint()</c> answers <c>{ ok: false }</c> rather than throwing,
    /// so a script that guards on <c>ok</c> behaves the same everywhere.
    /// </param>
    public ScriptHost(INamedQueryRunner queries, ICustomApiCaller? endpoints = null)
    {
        _queries = queries;
        _endpoints = endpoints;
    }

    public ScriptRunOutcome Run(string scriptBody, HookContext context, CancellationToken ct)
    {
        var messages = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var engine = new Jint.Engine(options =>
            {
                options.TimeoutInterval(Timeout);
                options.LimitMemory(MemoryLimitBytes);
                options.LimitRecursion(64);
                options.MaxStatements(5_000_000);
                options.Strict = true;
                options.CancellationToken(ct);
            });

            Bind(engine, context, messages);

            // The body is wrapped in an async function for two reasons: `return {...}` is the
            // natural way to answer, and `await api.query(...)` then reads identically here and
            // in the browser sandbox. A stray top-level declaration also cannot leak between runs.
            var wrapper = engine.Evaluate(
                "(async function (ctx, api, ui, utils) {\n" + scriptBody + "\n})");

            var returned = engine.Invoke(
                wrapper,
                engine.GetValue("ctx"),
                engine.GetValue("api"),
                engine.GetValue("ui"),
                engine.GetValue("utils"));

            // Drain the microtask queue, then take the value the promise settled with.
            // A rejection surfaces below as a PromiseRejectedException.
            engine.Advanced.ProcessTasks();
            returned = returned.UnwrapIfPromise();

            stopwatch.Stop();

            return ScriptRunOutcome.Success(
                ToHookResult(returned, engine),
                (int)stopwatch.ElapsedMilliseconds,
                messages);
        }
        catch (TimeoutException)
        {
            stopwatch.Stop();
            return ScriptRunOutcome.Expired((int)stopwatch.ElapsedMilliseconds, messages);
        }
        catch (ExecutionCanceledException)
        {
            stopwatch.Stop();
            return ScriptRunOutcome.Expired((int)stopwatch.ElapsedMilliseconds, messages);
        }
        catch (StatementsCountOverflowException)
        {
            // A runaway script that never yields. Same class of failure as the wall clock.
            stopwatch.Stop();
            return ScriptRunOutcome.Expired((int)stopwatch.ElapsedMilliseconds, messages);
        }
        catch (PromiseRejectedException ex)
        {
            // An async script that threw. Unwrapping its promise raises this.
            stopwatch.Stop();
            return ScriptRunOutcome.Failure(
                ex.RejectedValue?.ToString() ?? ex.Message, (int)stopwatch.ElapsedMilliseconds, messages);
        }
        catch (MemoryLimitExceededException ex)
        {
            stopwatch.Stop();
            return ScriptRunOutcome.Failure($"Memory limit exceeded: {ex.Message}", (int)stopwatch.ElapsedMilliseconds, messages);
        }
        catch (JavaScriptException ex)
        {
            stopwatch.Stop();
            return ScriptRunOutcome.Failure(
                $"{ex.Message} (line {ex.Location.Start.Line}, column {ex.Location.Start.Column})",
                (int)stopwatch.ElapsedMilliseconds,
                messages);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return ScriptRunOutcome.Failure(ex.Message, (int)stopwatch.ElapsedMilliseconds, messages);
        }
    }

    /// <summary>
    /// Builds ctx, api, ui and utils. The CLR delegates are installed under private names,
    /// captured by closure, then cleared from the global object so the script body can never
    /// reach them directly.
    /// </summary>
    private void Bind(Jint.Engine engine, HookContext context, List<string> messages)
    {
        var form = new Dictionary<string, object?>(context.Form, StringComparer.OrdinalIgnoreCase);

        engine.SetValue("__form", ToJs(engine, form));
        engine.SetValue("__value", ToJs(engine, context.Value));
        engine.SetValue("__response", ToJs(engine, context.Response));
        engine.SetValue("__hookKey", context.HookKey);
        engine.SetValue("__user", ToJs(engine, new
        {
            id = context.User.Id,
            name = context.User.Name,
            roles = context.User.Roles
        }));
        engine.SetValue("__tenant", ToJs(engine, new { id = context.Tenant.Id, code = context.Tenant.Code }));

        engine.SetValue("__query", new Func<string, JsValue, JsValue>((key, parameters) =>
        {
            var bag = JsToDictionary(parameters, engine);
            var result = _queries.RunAsync(key, bag, CancellationToken.None).GetAwaiter().GetResult();

            return ToJs(engine, new
            {
                ok = result.Ok,
                rows = result.Rows,
                columns = result.Columns,
                truncated = result.Truncated,
                error = result.Error
            });
        }));

        // api.callEndpoint(slug, params) — an endpoint written in the API Builder, run as the
        // signed-in user. Same answer shape as api.query so a script can treat them alike.
        engine.SetValue("__callEndpoint", new Func<string, JsValue, JsValue>((slug, parameters) =>
        {
            if (_endpoints is null)
            {
                return ToJs(engine, new
                {
                    ok = false,
                    rows = Array.Empty<object>(),
                    columns = Array.Empty<string>(),
                    truncated = false,
                    error = "Endpoints are not available in this run."
                });
            }

            var bag = JsToDictionary(parameters, engine);
            var result = _endpoints.RunAsync(slug, bag, CancellationToken.None).GetAwaiter().GetResult();

            return ToJs(engine, new
            {
                ok = result.Ok,
                rows = result.Rows,
                columns = result.Columns,
                truncated = result.Truncated,
                error = result.Error
            });
        }));

        engine.SetValue("__record", new Action<string>(text =>
        {
            if (messages.Count < 50) messages.Add(text ?? string.Empty);
        }));

        // ui on the server: there is no user at the other end of a server-side hook, so the
        // interactive primitives answer "no" rather than pretending. toast and error are
        // recorded and surfaced through the returned message and the hook log.
        engine.Execute("""
            var ctx, api, ui, utils;
            (function () {
              var query = __query;
              var callEndpoint = __callEndpoint;
              var record = __record;
              var formData = __form;

              ctx = {
                hookKey: __hookKey,
                form: formData,
                value: __value,
                response: __response,
                user: __user,
                tenant: __tenant,
                setForm: function (key, value) {
                  if (typeof key === 'object' && key !== null) {
                    for (var k in key) { if (Object.prototype.hasOwnProperty.call(key, k)) formData[k] = key[k]; }
                  } else {
                    formData[key] = value;
                  }
                  return formData;
                }
              };

              api = Object.freeze({
                query: function (queryKey, params) { return query(queryKey, params || {}); },
                callEndpoint: function (slug, params) { return callEndpoint(String(slug), params || {}); }
              });

              ui = Object.freeze({
                toast: function (message) { record(String(message)); },
                error: function (message) { record(String(message)); },
                confirm: function () { return false; },
                pickList: function () { return null; },
                openScreen: function () { return false; }
              });

              utils = Object.freeze({
                age: function (dob, asOf) {
                  if (!dob) return 0;
                  var birth = new Date(dob);
                  if (isNaN(birth.getTime())) return 0;
                  var on = asOf ? new Date(asOf) : new Date();
                  var years = on.getFullYear() - birth.getFullYear();
                  var m = on.getMonth() - birth.getMonth();
                  if (m < 0 || (m === 0 && on.getDate() < birth.getDate())) years--;
                  return years < 0 ? 0 : years;
                },
                formatDate: function (value) {
                  if (!value) return '';
                  var d = new Date(value);
                  if (isNaN(d.getTime())) return '';
                  var mm = String(d.getMonth() + 1);
                  var dd = String(d.getDate());
                  if (mm.length < 2) mm = '0' + mm;
                  if (dd.length < 2) dd = '0' + dd;
                  return d.getFullYear() + '-' + mm + '-' + dd;
                },
                round: function (value, digits) {
                  var n = Number(value);
                  if (isNaN(n)) return 0;
                  var f = Math.pow(10, digits === undefined ? 2 : digits);
                  return Math.round(n * f) / f;
                },
                isEmpty: function (value) {
                  if (value === null || value === undefined) return true;
                  if (typeof value === 'string') return value.trim().length === 0;
                  if (Array.isArray(value)) return value.length === 0;
                  return false;
                }
              });
            })();
            """);

        // Close the private doors. The closures above already hold what they need.
        engine.SetValue("__query", JsValue.Undefined);
        engine.SetValue("__callEndpoint", JsValue.Undefined);
        engine.SetValue("__record", JsValue.Undefined);
        engine.SetValue("__form", JsValue.Undefined);
        engine.SetValue("__value", JsValue.Undefined);
        engine.SetValue("__response", JsValue.Undefined);
        engine.SetValue("__user", JsValue.Undefined);
        engine.SetValue("__tenant", JsValue.Undefined);
        engine.SetValue("__hookKey", JsValue.Undefined);
    }

    /// <summary>Round-trips through JSON so only plain data crosses into the sandbox.</summary>
    private static JsValue ToJs(Jint.Engine engine, object? value)
    {
        if (value is null) return JsValue.Null;
        var json = JsonSerializer.Serialize(value, Json);
        return engine.Evaluate($"JSON.parse({JsonSerializer.Serialize(json, Json)})");
    }

    private static Dictionary<string, object?>? JsToDictionary(JsValue value, Jint.Engine engine)
    {
        if (value.IsNull() || value.IsUndefined()) return null;

        var json = engine.Evaluate("JSON.stringify").Call(value);
        if (json.IsNull() || json.IsUndefined()) return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json.AsString(), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HookResult ToHookResult(JsValue returned, Jint.Engine engine)
    {
        var result = HookResult.Empty();

        // The script may have edited ctx.form directly through ctx.setForm(); start there.
        var form = ReadDictionary(engine.Evaluate("ctx.form"), engine)
                   ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (returned.IsNull() || returned.IsUndefined() || !returned.IsObject())
        {
            if (form.Count > 0) result.Form = form;
            return result;
        }

        var payload = ReadDictionary(returned, engine);
        if (payload is null)
        {
            if (form.Count > 0) result.Form = form;
            return result;
        }

        if (payload.TryGetValue("cancelSave", out var cancelSave)) result.CancelSave = AsBool(cancelSave);
        if (payload.TryGetValue("cancelNavigation", out var cancelNav)) result.CancelNavigation = AsBool(cancelNav);
        if (payload.TryGetValue("redirectTo", out var redirect)) result.RedirectTo = redirect?.ToString();
        if (payload.TryGetValue("message", out var message)) result.Message = message?.ToString();

        // Fields the script returned, which the contract has always advertised:
        //
        //     return { form: { netSalary: 1234 } }
        //
        // Reading only ctx.form meant a script could compute a value, return it, get an
        // ok result — and have the answer silently discarded. The returned form wins over
        // the ctx.form snapshot: it is the script's last word on the subject.
        if (payload.TryGetValue("form", out var returnedForm)
            && returnedForm is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            foreach (var property in element.EnumerateObject())
                form[property.Name] = property.Value;   // JsonElement, as ReadDictionary yields
        }

        if (form.Count > 0) result.Form = form;

        return result;
    }

    private static Dictionary<string, object?>? ReadDictionary(JsValue value, Jint.Engine engine)
    {
        if (value.IsNull() || value.IsUndefined()) return null;

        try
        {
            var json = engine.Evaluate("JSON.stringify").Call(value);
            if (json.IsNull() || json.IsUndefined()) return null;
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json.AsString(), Json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool AsBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        JsonElement e => e.ValueKind == JsonValueKind.True,
        _ => bool.TryParse(value.ToString(), out var parsed) && parsed
    };
}

public sealed record ScriptRunOutcome(
    bool Ok,
    bool TimedOut,
    HookResult Result,
    string? Error,
    int DurationMs,
    IReadOnlyList<string> Messages)
{
    public static ScriptRunOutcome Success(HookResult result, int durationMs, IReadOnlyList<string> messages)
        => new(true, false, result, null, durationMs, messages);

    public static ScriptRunOutcome Failure(string error, int durationMs, IReadOnlyList<string> messages)
        => new(false, false, HookResult.Empty(), error, durationMs, messages);

    public static ScriptRunOutcome Expired(int durationMs, IReadOnlyList<string> messages)
        => new(false, true, HookResult.Empty(), $"The script exceeded its {ScriptHost.Timeout.TotalSeconds:0} second budget.", durationMs, messages);
}
