namespace HrSuite.Core.Abstractions;

/// <summary>
/// The tenant + user the current request runs as. Resolved once by middleware.
/// Repositories read this themselves so a caller cannot forget the tenant filter.
/// </summary>
public interface ITenantContext
{
    int TenantId { get; }
    string TenantCode { get; }
    string TenantName { get; }
    int UserId { get; }
    string UserName { get; }

    /// <summary>The employee this login belongs to, when one is linked. Drives self-approval checks.</summary>
    int? EmployeeId { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyCollection<string> Permissions { get; }
    bool IsResolved { get; }
    bool Has(string permission);
}
