namespace HrSuite.Extensions.Engine.Models;

/// <summary>
/// A row of ext_api_endpoint — an HTTP endpoint an administrator wrote, answering at
/// <c>/api/x/{slug}</c>. Unlike a named query it carries the SELECT itself, which is why
/// everything about it is validated twice: once when saved, once before every run.
/// </summary>
public sealed class CustomApiEndpoint
{
    public int EndpointId { get; set; }
    /// <summary>Null means the row applies to every tenant.</summary>
    public int? TenantId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string HttpMethod { get; set; } = "POST";
    public string SqlText { get; set; } = string.Empty;
    public string? ParamsJson { get; set; }
    public string? ColumnsJson { get; set; }
    public int MaxRows { get; set; } = 100;
    public string? RequiredPermission { get; set; }
    public bool IsActive { get; set; }
    public int VersionNo { get; set; } = 1;
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }
}

/// <summary>
/// A declared parameter. The runner binds these and nothing else, so a caller who sends
/// an extra key changes nothing about the query that runs.
/// </summary>
public sealed class CustomApiParam
{
    public string Name { get; set; } = string.Empty;
    /// <summary>string | int | decimal | bool | date</summary>
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    /// <summary>Used by the admin screen's test run only. Never read at runtime.</summary>
    public string? Sample { get; set; }
}

public sealed class CustomApiVersion
{
    public int HistoryId { get; set; }
    public int EndpointId { get; set; }
    public int VersionNo { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string HttpMethod { get; set; } = string.Empty;
    public string SqlText { get; set; } = string.Empty;
    public string? ParamsJson { get; set; }
    public string? ColumnsJson { get; set; }
    public int MaxRows { get; set; }
    public string? RequiredPermission { get; set; }
    public bool IsActive { get; set; }
    public int? ArchivedBy { get; set; }
    public DateTime? ArchivedOn { get; set; }
}

public sealed class CustomApiCallLogEntry
{
    public long LogId { get; set; }
    public int? EndpointId { get; set; }
    public int? TenantId { get; set; }
    public string? Slug { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public int RowCount { get; set; }
    public string? Message { get; set; }
    public int? CalledBy { get; set; }
    public DateTime CalledOn { get; set; }
}

public static class CustomApiCallStatus
{
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Denied = "denied";
}

/// <summary>What the API Builder screen posts when saving an endpoint.</summary>
public sealed class CustomApiSaveRequest
{
    public int EndpointId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string HttpMethod { get; set; } = "POST";
    public string SqlText { get; set; } = string.Empty;
    public IReadOnlyList<CustomApiParam> Params { get; set; } = Array.Empty<CustomApiParam>();
    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
    public int MaxRows { get; set; } = 100;
    public string? RequiredPermission { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Writes tenant_id = NULL. Gated on the admin.tenant permission.</summary>
    public bool ApplyToAllTenants { get; set; }
}

/// <summary>
/// The "Test" round trip. Save stays disabled until this passes, exactly as it does for a
/// script hook: an endpoint nobody has run once should not be reachable on a URL.
/// </summary>
public sealed class CustomApiTestRequest
{
    public string SqlText { get; set; } = string.Empty;
    public IReadOnlyList<CustomApiParam> Params { get; set; } = Array.Empty<CustomApiParam>();
    /// <summary>Empty means "return every column", which is what a first test run needs.</summary>
    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
    public int MaxRows { get; set; } = 25;
}

/// <summary>
/// What both the runtime endpoint and the admin test return. Mirrors NamedQueryResult so a
/// caller that already handles api.query() handles this without a second code path.
/// </summary>
public sealed record CustomApiResult(
    bool Ok,
    IReadOnlyList<IDictionary<string, object?>> Rows,
    IReadOnlyList<string> Columns,
    bool Truncated,
    int DurationMs = 0,
    string? Error = null)
{
    public static CustomApiResult Failure(string error, int durationMs = 0)
        => new(false, Array.Empty<IDictionary<string, object?>>(), Array.Empty<string>(), false, durationMs, error);
}
