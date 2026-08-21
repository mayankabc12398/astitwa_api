namespace HrSuite.Common.Results;

/// <summary>A single failure. <paramref name="Field"/> is set for field-level validation errors.</summary>
public sealed record Error(string Code, string Message, string? Field = null)
{
    public static Error Validation(string message, string? field = null) => new(ErrorCode.Validation, message, field);
    public static Error NotFound(string message)   => new(ErrorCode.NotFound, message);
    public static Error Conflict(string message)   => new(ErrorCode.Conflict, message);
    public static Error Forbidden(string message)  => new(ErrorCode.Forbidden, message);
}
