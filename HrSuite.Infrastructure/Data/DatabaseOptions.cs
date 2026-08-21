namespace HrSuite.Infrastructure.Data;

/// <summary>
/// Bound from configuration. The connection string itself comes from an environment
/// variable or user-secrets — never from a file under source control (section 11).
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Seconds. Applied to every stored-procedure call.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
