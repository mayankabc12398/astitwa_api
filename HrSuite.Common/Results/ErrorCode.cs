namespace HrSuite.Common.Results;

/// <summary>Stable, client-facing error identifiers. Never leak exception text.</summary>
public static class ErrorCode
{
    public const string Validation      = "VALIDATION_FAILED";
    public const string NotFound        = "NOT_FOUND";
    public const string Conflict        = "CONFLICT";
    public const string Forbidden       = "FORBIDDEN";
    public const string Unauthorized    = "UNAUTHORIZED";
    public const string ModuleDisabled  = "MODULE_DISABLED";
    public const string Unexpected      = "UNEXPECTED";
}
