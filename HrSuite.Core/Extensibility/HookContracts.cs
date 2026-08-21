namespace HrSuite.Core.Extensibility;

/// <summary>The <c>ctx</c> object handed to a script. Nothing else is reachable from a script.</summary>
public sealed class HookContext
{
    public string HookKey { get; init; } = string.Empty;
    public IDictionary<string, object?> Form { get; init; } = new Dictionary<string, object?>();
    public object? Value { get; init; }
    public object? Response { get; init; }
    public HookUser User { get; init; } = new();
    public HookTenant Tenant { get; init; } = new();
}

public sealed class HookUser
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();
}

public sealed class HookTenant
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
}

/// <summary>
/// What a script may ask the host to do. Every field is optional; an empty object
/// means "behave exactly as the base code was written".
/// </summary>
public sealed class HookResult
{
    public bool CancelSave { get; set; }
    public bool CancelNavigation { get; set; }
    public string? RedirectTo { get; set; }
    public string? Message { get; set; }
    /// <summary>Fields the script mutated via <c>ctx.setForm()</c>.</summary>
    public IDictionary<string, object?>? Form { get; set; }

    public static HookResult Empty() => new();
    public bool IsEmpty => !CancelSave && !CancelNavigation && RedirectTo is null && Message is null && (Form is null || Form.Count == 0);
}
