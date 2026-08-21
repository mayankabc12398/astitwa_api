namespace HrSuite.Core.Abstractions;

/// <summary>
/// Backs <c>api.query(queryKey, params)</c>. A script names a registered key; it never
/// supplies SQL, a procedure name, or an undeclared parameter. Output columns outside the
/// declared whitelist are stripped before the rows leave this boundary.
/// </summary>
public interface INamedQueryRunner
{
    Task<NamedQueryResult> RunAsync(string queryKey, IDictionary<string, object?>? parameters, CancellationToken ct = default);
}

public sealed record NamedQueryResult(
    bool Ok,
    IReadOnlyList<IDictionary<string, object?>> Rows,
    IReadOnlyList<string> Columns,
    bool Truncated,
    string? Error = null)
{
    public static NamedQueryResult Failure(string error)
        => new(false, Array.Empty<IDictionary<string, object?>>(), Array.Empty<string>(), false, error);
}
