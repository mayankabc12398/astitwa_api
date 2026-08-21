namespace HrSuite.Core.Abstractions;

/// <summary>
/// A unique constraint rejected a write.
///
/// The database is the only place a uniqueness rule can be enforced without a race: a
/// pre-check ("does this code exist?") is a read, and two requests can pass it at the same
/// instant. So the pre-check stays — it produces the good message, naming the field — and
/// this exception covers the case the pre-check cannot: the collision that happens between
/// the read and the write, or on a table nobody wrote a pre-check for.
///
/// Declared in Core, not Infrastructure, so that a service can catch it without Layer 1
/// business code learning what MySQL is. <c>RepositoryBase</c> does the translation.
/// </summary>
public sealed class DuplicateKeyException : Exception
{
    public DuplicateKeyException(string constraintName, string procName, Exception inner)
        : base($"A unique constraint ({constraintName}) rejected {procName}.", inner)
    {
        ConstraintName = constraintName;
        ProcName = procName;
    }

    /// <summary>The database's name for the index, e.g. <c>uk_hr_department</c>.</summary>
    public string ConstraintName { get; }

    public string ProcName { get; }
}
