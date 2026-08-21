using HrSuite.Core.Abstractions;
using HrSuite.Core.Extensibility;

namespace HrSuite.Infrastructure.Extensibility;

/// <summary>
/// The Layer 1 default for the extension boundary.
///
/// Base code calls IHookEngine unconditionally. When no Layer 5 assembly is deployed this
/// implementation answers, returning an empty result — exactly what "no script registered"
/// means. Screens then behave precisely as written, with no null checks scattered around.
///
/// A deployed HrSuite.Extensions.Engine registers over this at startup.
/// </summary>
public sealed class NullHookEngine : IHookEngine
{
    public Task<HookResult> RunAsync(string hookKey, HookContext context, CancellationToken ct = default)
        => Task.FromResult(HookResult.Empty());

    public Task<IReadOnlyList<ClientHookScript>> GetClientScriptsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ClientHookScript>>(Array.Empty<ClientHookScript>());
}

/// <summary>Same idea for named queries: with no engine deployed, every key is unknown.</summary>
public sealed class NullNamedQueryRunner : INamedQueryRunner
{
    public Task<NamedQueryResult> RunAsync(string queryKey, IDictionary<string, object?>? parameters, CancellationToken ct = default)
        => Task.FromResult(NamedQueryResult.Failure("No extension engine is deployed."));
}
