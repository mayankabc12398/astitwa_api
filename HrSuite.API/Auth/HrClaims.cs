namespace HrSuite.API.Auth;

/// <summary>Claim types the product adds on top of the standard set.</summary>
public static class HrClaims
{
    public const string TenantId   = "hrs:tid";
    public const string TenantCode = "hrs:tcode";
    public const string TenantName = "hrs:tname";
    public const string EmployeeId = "hrs:eid";
    public const string Permission = "hrs:perm";
}
