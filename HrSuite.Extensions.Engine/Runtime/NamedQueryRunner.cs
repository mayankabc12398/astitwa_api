using System.Globalization;
using System.Text.Json;
using HrSuite.Core.Abstractions;
using HrSuite.Extensions.Engine.Data;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace HrSuite.Extensions.Engine.Runtime;

/// <summary>
/// Everything a script can reach in the database goes through here (section 10.5).
///
///   * The key must exist in ext_named_query and be active for this tenant.
///   * Only the declared parameters are bound; anything else the script sends is dropped.
///   * Only the declared columns come back; anything else the procedure returns is stripped.
///   * The row count is capped by the registration.
///   * The registration may demand a permission, checked against the caller's own claims.
///
/// A script never supplies SQL, a procedure name, or an undeclared parameter.
/// </summary>
public sealed class NamedQueryRunner : INamedQueryRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly NamedQueryRepository _repository;
    private readonly ITenantContext _tenant;
    private readonly ILogger<NamedQueryRunner> _log;

    public NamedQueryRunner(NamedQueryRepository repository, ITenantContext tenant, ILogger<NamedQueryRunner> log)
    {
        _repository = repository;
        _tenant = tenant;
        _log = log;
    }

    public async Task<NamedQueryResult> RunAsync(
        string queryKey, IDictionary<string, object?>? parameters, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryKey)) return NamedQueryResult.Failure("A query key is required.");

        var definition = await _repository.GetByKeyAsync(queryKey.Trim(), ct).ConfigureAwait(false);
        if (definition is null || !definition.IsActive)
            return NamedQueryResult.Failure($"'{queryKey}' is not a registered query.");

        if (!NamedQueryRepository.IsValidProcName(definition.ProcName))
            return NamedQueryResult.Failure($"'{queryKey}' is registered against an invalid procedure name.");

        // A script runs under the calling user's permissions, checked here on the server.
        if (!string.IsNullOrWhiteSpace(definition.RequiredPermission) && !_tenant.Has(definition.RequiredPermission!))
            return NamedQueryResult.Failure($"You do not have permission to run '{queryKey}'.");

        var declaredParams = Deserialise<List<NamedQueryParam>>(definition.ParamsJson) ?? new List<NamedQueryParam>();
        var declaredColumns = Deserialise<List<string>>(definition.ColumnsJson) ?? new List<string>();

        var args = ProcArgs.New();
        var supplied = parameters ?? new Dictionary<string, object?>();

        foreach (var declared in declaredParams)
        {
            if (string.IsNullOrWhiteSpace(declared.Name)) continue;

            var present = TryFind(supplied, declared.Name, out var raw);
            if (declared.Required && (!present || raw is null))
                return NamedQueryResult.Failure($"'{declared.Name}' is required by '{queryKey}'.");

            try
            {
                args.Set(declared.Name, present ? Coerce(raw, declared.Type) : null);
            }
            catch (InvalidOperationException)
            {
                // The registration declared a parameter the repository base owns — tenant or
                // user. Refuse the whole query rather than let it run half-bound.
                return NamedQueryResult.Failure(
                    $"'{queryKey}' declares '{declared.Name}', which the tenant filter owns. Remove it from the registration.");
            }
        }

        IReadOnlyList<IDictionary<string, object?>> rows;
        try
        {
            rows = await _repository.ExecuteRegisteredAsync(definition.ProcName, args, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The caller sees a stable message; the detail goes to the log.
            _log.LogError(ex, "Named query {Key} failed for tenant {TenantId}.", queryKey, _tenant.TenantId);
            return NamedQueryResult.Failure($"'{queryKey}' could not be run.");
        }

        var maxRows = definition.MaxRows > 0 ? definition.MaxRows : 100;
        var truncated = rows.Count > maxRows;
        var page = truncated ? rows.Take(maxRows).ToList() : rows;

        // With no whitelist declared, nothing is exposed. Silence is not permission.
        var whitelist = declaredColumns
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projected = page
            .Select(row => (IDictionary<string, object?>)row
                .Where(kv => whitelist.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new NamedQueryResult(true, projected, whitelist.ToList(), truncated);
    }

    private static bool TryFind(IDictionary<string, object?> supplied, string name, out object? value)
    {
        if (supplied.TryGetValue(name, out value)) return true;

        foreach (var (key, candidate) in supplied)
        {
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = candidate;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Turns whatever the script passed into the declared type, or null.</summary>
    private static object? Coerce(object? raw, string type)
    {
        if (raw is null) return null;
        var text = raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture);

        return type.ToLowerInvariant() switch
        {
            "int" => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null,
            "decimal" => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null,
            "bool" => ParseBool(text),
            "date" => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null,
            _ => text
        };
    }

    private static object? ParseBool(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "y" or "on" => true,
        "0" or "false" or "no" or "n" or "off" => false,
        _ => null
    };

    private static T? Deserialise<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
