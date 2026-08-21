namespace HrSuite.Common.Results;

/// <summary>
/// The one shape every endpoint returns, success or failure (section 11). A raw exception
/// or a stack trace never reaches the client.
///
/// It lives in Common rather than in the API host so that add-on and extension assemblies
/// can answer in the same envelope without referencing the host — which would invert the
/// dependency rule and make the plugin build circular.
/// </summary>
public sealed class ApiResponse
{
    public bool Success { get; init; }
    public object? Data { get; init; }
    public ApiError? Error { get; init; }
    public string TraceId { get; init; } = string.Empty;

    public static ApiResponse Ok(object? data, string traceId)
        => new() { Success = true, Data = data, TraceId = traceId };

    public static ApiResponse Fail(string code, string message, string traceId, IReadOnlyList<ApiFieldError>? fields = null)
        => new()
        {
            Success = false,
            TraceId = traceId,
            Error = new ApiError { Code = code, Message = message, Fields = fields ?? Array.Empty<ApiFieldError>() }
        };

    public static ApiResponse FromResult(Result result, string traceId)
    {
        if (result.IsSuccess) return Ok(null, traceId);

        var first = result.FirstError!;
        var fields = result.Errors
            .Where(e => e.Field is not null)
            .Select(e => new ApiFieldError { Field = e.Field!, Message = e.Message })
            .ToList();

        return Fail(first.Code, first.Message, traceId, fields);
    }
}

public sealed class ApiError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<ApiFieldError> Fields { get; init; } = Array.Empty<ApiFieldError>();
}

public sealed class ApiFieldError
{
    public string Field { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>Maps a stable error code to the HTTP status that carries it.</summary>
public static class ErrorStatus
{
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int ServerError = 500;

    public static int For(string code) => code switch
    {
        ErrorCode.Validation     => BadRequest,
        ErrorCode.NotFound       => NotFound,
        ErrorCode.Conflict       => Conflict,
        ErrorCode.Unauthorized   => Unauthorized,
        ErrorCode.Forbidden      => Forbidden,
        ErrorCode.ModuleDisabled => Forbidden,
        _                        => ServerError
    };
}
