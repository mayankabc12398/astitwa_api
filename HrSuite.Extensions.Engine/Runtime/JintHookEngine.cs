using System.Text.Json;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Extensibility;
using HrSuite.Extensions.Engine.Data;
using HrSuite.Extensions.Engine.Models;
using Microsoft.Extensions.Logging;

namespace HrSuite.Extensions.Engine.Runtime;

/// <summary>
/// Layer 5, server half. Replaces the Layer 1 null engine when this assembly is deployed.
///
/// The contract base code relies on: every script runs inside try/catch, a failure is written
/// to ext_hook_log, and the caller receives the same empty result it would have received had
/// no script existed. A broken script must never block a save (section 10.5).
/// </summary>
public sealed class JintHookEngine : IHookEngine
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ScriptHookRepository _hooks;
    private readonly HookLogRepository _log;
    private readonly ScriptHost _host;
    private readonly ILogger<JintHookEngine> _logger;

    public JintHookEngine(
        ScriptHookRepository hooks,
        HookLogRepository log,
        ScriptHost host,
        ILogger<JintHookEngine> logger)
    {
        _hooks = hooks;
        _log = log;
        _host = host;
        _logger = logger;
    }

    public async Task<HookResult> RunAsync(string hookKey, HookContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hookKey)) return HookResult.Empty();

        IReadOnlyList<ScriptHook> scripts;
        try
        {
            scripts = await _hooks.ActiveForKeyAsync(hookKey, RunTarget.Server, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Even the lookup failing must not surface to the caller.
            _logger.LogError(ex, "Could not load hooks for {HookKey}.", hookKey);
            return HookResult.Empty();
        }

        if (scripts.Count == 0) return HookResult.Empty();

        var combined = HookResult.Empty();

        foreach (var script in scripts)
        {
            var outcome = _host.Run(script.ScriptBody, context, ct);

            await RecordAsync(script, hookKey, outcome, context, ct).ConfigureAwait(false);

            if (!outcome.Ok) continue; // a failed script is treated as absent

            Merge(combined, outcome.Result, outcome.Messages);

            // A script that stops the save ends the chain; later scripts have nothing to add.
            if (combined.CancelSave) break;
        }

        return combined;
    }

    public async Task<IReadOnlyList<ClientHookScript>> GetClientScriptsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _hooks.ClientScriptsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load client hook scripts.");
            return Array.Empty<ClientHookScript>();
        }
    }

    /// <summary>Runs an unsaved body against sample data. Backs "Test with sample data" (section 10.6).</summary>
    public ScriptRunOutcome Test(string scriptBody, HookContext context, CancellationToken ct)
        => _host.Run(scriptBody, context, ct);

    private static void Merge(HookResult target, HookResult source, IReadOnlyList<string> messages)
    {
        target.CancelSave |= source.CancelSave;
        target.CancelNavigation |= source.CancelNavigation;
        target.RedirectTo ??= source.RedirectTo;

        var message = source.Message ?? (messages.Count > 0 ? string.Join(" ", messages) : null);
        if (message is not null) target.Message = target.Message is null ? message : $"{target.Message} {message}";

        if (source.Form is not { Count: > 0 }) return;

        target.Form ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source.Form) target.Form[key] = value;
    }

    private async Task RecordAsync(
        ScriptHook script, string hookKey, ScriptRunOutcome outcome, HookContext context, CancellationToken ct)
    {
        var status = outcome switch
        {
            { TimedOut: true } => HookLogStatus.Timeout,
            { Ok: false } => HookLogStatus.Error,
            _ => HookLogStatus.Ok
        };

        var message = outcome.Error ?? (outcome.Messages.Count > 0 ? string.Join(" | ", outcome.Messages) : null);

        try
        {
            await _log.WriteAsync(
                script.HookId, hookKey, RunTarget.Server, status, outcome.DurationMs,
                message, SafeContext(context), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // If even the audit write fails, the business operation still proceeds.
            _logger.LogError(ex, "Could not write hook log for {HookKey}.", hookKey);
        }

        if (!outcome.Ok)
        {
            _logger.LogWarning("Hook {HookKey} (id {HookId}) {Status}: {Message}",
                hookKey, script.HookId, status, message);
        }
    }

    private static string? SafeContext(HookContext context)
    {
        try
        {
            return JsonSerializer.Serialize(new { context.HookKey, context.Form, context.Value }, Json);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
