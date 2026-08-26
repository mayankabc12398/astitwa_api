using HrSuite.Extensions.Engine.Models;

namespace HrSuite.Extensions.Engine.Runtime;

/// <summary>
/// What a script may ask of the API Builder: run one endpoint by slug, as the signed-in
/// user, with the endpoint's own permission check applied. This is the server-side twin of
/// <c>api.callEndpoint()</c> in the browser sandbox, so a script reads the same in both.
///
/// Kept inside the engine rather than in Core on purpose — base code has no business
/// calling an endpoint an administrator wrote.
/// </summary>
public interface ICustomApiCaller
{
    Task<CustomApiResult> RunAsync(string slug, IDictionary<string, object?>? supplied, CancellationToken ct = default);
}
