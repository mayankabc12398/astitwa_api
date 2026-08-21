using HrSuite.Core.Extensibility;

namespace HrSuite.Core.Abstractions;

/// <summary>
/// Layer 5 contract. Base code calls this at every hook slot and acts on the returned object.
/// With no script registered the engine returns an empty result — the screen behaves as written.
/// A script that throws is logged and treated as absent. It must never block a save.
/// </summary>
public interface IHookEngine
{
    Task<HookResult> RunAsync(string hookKey, HookContext context, CancellationToken ct = default);

    /// <summary>Client-side script bodies for the current tenant, sent to the browser sandbox.</summary>
    Task<IReadOnlyList<ClientHookScript>> GetClientScriptsAsync(CancellationToken ct = default);
}

public sealed record ClientHookScript(int HookId, string HookKey, int SeqNo, string ScriptBody, int? DebounceMs);
