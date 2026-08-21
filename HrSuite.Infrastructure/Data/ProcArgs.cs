using Dapper;

namespace HrSuite.Infrastructure.Data;

/// <summary>
/// Parameters for one stored-procedure call.
///
/// Callers name a parameter without its <c>p_</c> prefix; the prefix is added here so the
/// convention cannot drift. The tenant and the acting user are NOT settable — the
/// repository base stamps them at execution time so no caller can omit or fake them.
/// </summary>
public sealed class ProcArgs
{
    internal const string TenantParam = "p_tenant_id";
    internal const string UserParam = "p_user_id";

    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _allowScopeParams;

    private ProcArgs(bool allowScopeParams = false) => _allowScopeParams = allowScopeParams;

    public static ProcArgs New() => new();

    /// <summary>
    /// Only for <see cref="UnscopedRepositoryBase"/>, where no tenant context exists yet and the
    /// tenant id must be passed explicitly. Internal on purpose: no controller or service can reach it.
    /// </summary>
    internal static ProcArgs NewUnscoped() => new(allowScopeParams: true);

    public static ProcArgs From(params (string Name, object? Value)[] pairs)
    {
        var args = new ProcArgs();
        foreach (var (name, value) in pairs) args.Set(name, value);
        return args;
    }

    public ProcArgs Set(string name, object? value)
    {
        var key = Normalise(name);

        if (key is TenantParam or UserParam && !_allowScopeParams)
        {
            throw new InvalidOperationException(
                $"'{key}' is stamped by the repository base from the request's tenant context. " +
                "Setting it by hand would let a caller read another tenant's rows.");
        }

        _values[key] = value;
        return this;
    }

    /// <summary>Adds the value only when it is present. Keeps optional filters readable.</summary>
    public ProcArgs SetIf(bool condition, string name, object? value)
        => condition ? Set(name, value) : this;

    internal DynamicParameters ToDynamicParameters(int tenantId, int userId)
    {
        var parameters = new DynamicParameters();
        foreach (var (key, value) in _values) parameters.Add(key, value);
        parameters.Add(TenantParam, tenantId);
        parameters.Add(UserParam, userId);
        return parameters;
    }

    /// <summary>For the few procedures that run before a tenant exists (login).</summary>
    internal DynamicParameters ToDynamicParametersWithoutTenant()
    {
        var parameters = new DynamicParameters();
        foreach (var (key, value) in _values) parameters.Add(key, value);
        return parameters;
    }

    private static string Normalise(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Parameter name is required.", nameof(name));
        return name.StartsWith("p_", StringComparison.OrdinalIgnoreCase) ? name : "p_" + name;
    }
}
