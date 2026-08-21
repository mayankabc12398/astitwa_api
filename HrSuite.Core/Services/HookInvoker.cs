using System.Reflection;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Extensibility;

namespace HrSuite.Core.Services;

/// <summary>
/// Small helper that keeps the hook call site in each service down to one readable line.
/// It builds the ctx object, calls the engine and never lets a script failure escape —
/// a broken script must not block a save (section 10.5).
/// </summary>
public sealed class HookInvoker
{
    private readonly IHookEngine _engine;
    private readonly ITenantContext _tenant;

    public HookInvoker(IHookEngine engine, ITenantContext tenant)
    {
        _engine = engine;
        _tenant = tenant;
    }

    public async Task<HookResult> RunAsync(
        string hookKey,
        object? form = null,
        object? value = null,
        object? response = null,
        CancellationToken ct = default)
    {
        var context = new HookContext
        {
            HookKey = hookKey,
            Form = ToDictionary(form),
            Value = value,
            Response = response,
            User = new HookUser { Id = _tenant.UserId, Name = _tenant.UserName, Roles = _tenant.Roles.ToArray() },
            Tenant = new HookTenant { Id = _tenant.TenantId, Code = _tenant.TenantCode }
        };

        // The engine already swallows script errors; this guard covers the engine itself
        // being unavailable, so base code keeps working with no extension deployed at all.
        try
        {
            return await _engine.RunAsync(hookKey, context, ct).ConfigureAwait(false);
        }
        catch
        {
            return HookResult.Empty();
        }
    }

    /// <summary>
    /// Flattens an entity into the plain object a script sees. Scripts get values, never a
    /// live reference to a domain object they could mutate behind the service's back.
    /// </summary>
    private static IDictionary<string, object?> ToDictionary(object? source)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return map;

        if (source is IDictionary<string, object?> existing)
        {
            foreach (var (key, value) in existing) map[key] = value;
            return map;
        }

        foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead) continue;
            map[Camel(property.Name)] = property.GetValue(source);
        }

        return map;
    }

    private static string Camel(string name)
        => name.Length > 0 && char.IsUpper(name[0]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;
}
