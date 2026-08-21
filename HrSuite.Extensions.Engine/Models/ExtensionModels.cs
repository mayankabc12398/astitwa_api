namespace HrSuite.Extensions.Engine.Models;

/// <summary>A row of ext_script_hook.</summary>
public sealed class ScriptHook
{
    public int HookId { get; set; }
    /// <summary>Null means the row applies to every tenant.</summary>
    public int? TenantId { get; set; }
    public string HookKey { get; set; } = string.Empty;
    public int SeqNo { get; set; } = 10;
    public string RunOn { get; set; } = RunTarget.Server;
    public string ScriptBody { get; set; } = string.Empty;
    public int? DebounceMs { get; set; }
    public bool IsActive { get; set; }
    public int VersionNo { get; set; } = 1;
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }
}

public static class RunTarget
{
    public const string Client = "client";
    public const string Server = "server";

    public static bool IsValid(string? value)
        => value is Client or Server;
}

public sealed class ScriptHookVersion
{
    public int HistoryId { get; set; }
    public int HookId { get; set; }
    public int VersionNo { get; set; }
    public string RunOn { get; set; } = string.Empty;
    public int SeqNo { get; set; }
    public int? DebounceMs { get; set; }
    public bool IsActive { get; set; }
    public string ScriptBody { get; set; } = string.Empty;
    public int? ArchivedBy { get; set; }
    public DateTime? ArchivedOn { get; set; }
}

/// <summary>A row of ext_named_query — the whitelist a script may reach through api.query().</summary>
public sealed class NamedQueryDefinition
{
    public int QueryId { get; set; }
    public int? TenantId { get; set; }
    public string QueryKey { get; set; } = string.Empty;
    public string ProcName { get; set; } = string.Empty;
    public string? ParamsJson { get; set; }
    public string? ColumnsJson { get; set; }
    public int MaxRows { get; set; } = 100;
    public string? RequiredPermission { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A declared parameter. Anything a script sends that is not declared here is dropped.</summary>
public sealed class NamedQueryParam
{
    public string Name { get; set; } = string.Empty;
    /// <summary>string | int | decimal | bool | date</summary>
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
}

public sealed class HookLogEntry
{
    public long LogId { get; set; }
    public int? HookId { get; set; }
    public int? TenantId { get; set; }
    public string? HookKey { get; set; }
    public string? RunOn { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public string? Message { get; set; }
    public string? ContextJson { get; set; }
    public int? LoggedBy { get; set; }
    public DateTime LoggedOn { get; set; }
}

public static class HookLogStatus
{
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Timeout = "timeout";
}

/// <summary>What the admin screen posts when saving a hook.</summary>
public sealed class ScriptHookSaveRequest
{
    public int HookId { get; set; }
    public string HookKey { get; set; } = string.Empty;
    public int SeqNo { get; set; } = 10;
    public string RunOn { get; set; } = RunTarget.Server;
    public string ScriptBody { get; set; } = string.Empty;
    public int? DebounceMs { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Writes tenant_id = NULL. Gated on the admin.tenant permission.</summary>
    public bool ApplyToAllTenants { get; set; }
}

public sealed class NamedQuerySaveRequest
{
    public int QueryId { get; set; }
    public string QueryKey { get; set; } = string.Empty;
    public string ProcName { get; set; } = string.Empty;
    public IReadOnlyList<NamedQueryParam> Params { get; set; } = Array.Empty<NamedQueryParam>();
    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
    public int MaxRows { get; set; } = 100;
    public string? RequiredPermission { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ApplyToAllTenants { get; set; }
}

/// <summary>The "Test with sample data" round trip. Save stays disabled until this passes.</summary>
public sealed class ScriptTestRequest
{
    public string HookKey { get; set; } = string.Empty;
    public string RunOn { get; set; } = RunTarget.Server;
    public string ScriptBody { get; set; } = string.Empty;
    public string? SampleFormJson { get; set; }
}

public sealed class ScriptTestResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public object? Result { get; set; }
    public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();
}
