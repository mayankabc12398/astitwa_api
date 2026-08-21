namespace HrSuite.Common.Results;

/// <summary>Outcome without a payload.</summary>
public class Result
{
    protected Result(bool ok, IReadOnlyList<Error> errors) { IsSuccess = ok; Errors = errors; }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public IReadOnlyList<Error> Errors { get; }
    public Error? FirstError => Errors.Count > 0 ? Errors[0] : null;

    public static Result Success() => new(true, Array.Empty<Error>());
    public static Result Fail(params Error[] errors) => new(false, errors);
    public static Result Fail(string code, string message) => new(false, new[] { new Error(code, message) });
    public static Result Invalid(string message, string? field = null) => Fail(Error.Validation(message, field));
}

/// <summary>Outcome carrying a payload on success.</summary>
public sealed class Result<T> : Result
{
    private Result(bool ok, T? value, IReadOnlyList<Error> errors) : base(ok, errors) => Value = value;

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, Array.Empty<Error>());
    public static new Result<T> Fail(params Error[] errors) => new(false, default, errors);
    public static new Result<T> Fail(string code, string message) => new(false, default, new[] { new Error(code, message) });
    public static new Result<T> Invalid(string message, string? field = null) => Fail(Error.Validation(message, field));
    public static Result<T> NotFound(string message) => Fail(Error.NotFound(message));

    public static implicit operator Result<T>(T value) => Success(value);
}
