using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;

namespace HrSuite.API.Middleware;

/// <summary>
/// Last line of defence. Anything that escapes a controller becomes a generic envelope with a
/// trace id; the detail goes to the log, never to the client (section 11).
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller went away. Nothing to report.
        }
        catch (DuplicateKeyException ex)
        {
            // A unique index rejected the write. That is the caller's input, not a fault, so
            // it must not read as one. A service that knows which field is at fault catches
            // this first and says so precisely; this is the backstop for the ones that do not,
            // so a table added later cannot answer a duplicate with a 500.
            _log.LogInformation(ex, "Unique constraint {Constraint} rejected {Proc}.", ex.ConstraintName, ex.ProcName);

            await WriteAsync(context, StatusCodes.Status409Conflict, ApiResponse.Fail(
                ErrorCode.Validation,
                "A record with these details already exists.",
                context.TraceIdentifier));
        }
        catch (Exception ex)
        {
            var traceId = context.TraceIdentifier;
            _log.LogError(ex, "Unhandled exception on {Method} {Path}. TraceId {TraceId}.",
                context.Request.Method, context.Request.Path, traceId);

            await WriteAsync(context, StatusCodes.Status500InternalServerError, ApiResponse.Fail(
                ErrorCode.Unexpected,
                "Something went wrong. Quote the trace id when reporting this.",
                traceId));
        }
    }

    private async Task WriteAsync(HttpContext context, int statusCode, ApiResponse body)
    {
        if (context.Response.HasStarted)
        {
            _log.LogWarning("Response already started; cannot write the error envelope for {TraceId}.",
                context.TraceIdentifier);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
