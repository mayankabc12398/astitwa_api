using System.Runtime.CompilerServices;
using HrSuite.Common.Results;

namespace HrSuite.Common.Guards;

/// <summary>Cheap argument checks. Throws only for programmer error, never for user input.</summary>
public static class Guard
{
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null) where T : class
        => value ?? throw new ArgumentNullException(name);

    public static string NotBlank(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", name) : value;

    public static int Positive(int value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "Value must be positive.");
}

/// <summary>Accumulates user-facing validation errors without throwing.</summary>
public sealed class Validator
{
    private readonly List<Error> _errors = new();

    public Validator Require(bool condition, string message, string? field = null)
    {
        if (!condition) _errors.Add(Error.Validation(message, field));
        return this;
    }

    public Validator RequireText(string? value, string message, string? field = null)
        => Require(!string.IsNullOrWhiteSpace(value), message, field);

    public bool HasErrors => _errors.Count > 0;
    public Result ToResult() => HasErrors ? Result.Fail(_errors.ToArray()) : Result.Success();
    public Result<T> ToResult<T>(Func<T> onValid) => HasErrors ? Result<T>.Fail(_errors.ToArray()) : Result<T>.Success(onValid());
}
