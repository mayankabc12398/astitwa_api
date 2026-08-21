using System.Security.Claims;
using HrSuite.Core.Abstractions;

namespace HrSuite.API.Auth;

/// <summary>
/// Reads the tenant and user straight off the validated token. There is no header, query
/// string or route segment that can change it, so a caller cannot ask for another tenant's data.
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly ClaimsPrincipal? _principal;

    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        _principal = accessor.HttpContext?.User;

        if (_principal?.Identity?.IsAuthenticated != true) return;

        TenantId = ReadInt(HrClaims.TenantId);
        TenantCode = Read(HrClaims.TenantCode) ?? string.Empty;
        TenantName = Read(HrClaims.TenantName) ?? string.Empty;
        UserId = ReadInt(ClaimTypes.NameIdentifier);
        UserName = Read(ClaimTypes.Name) ?? string.Empty;
        EmployeeId = ReadNullableInt(HrClaims.EmployeeId);

        Roles = _principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        _permissions = _principal.FindAll(HrClaims.Permission)
                                 .Select(c => c.Value)
                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Permissions = _permissions.ToArray();

        IsResolved = TenantId > 0 && UserId > 0;
    }

    private readonly HashSet<string> _permissions = new(StringComparer.OrdinalIgnoreCase);

    public int TenantId { get; }
    public string TenantCode { get; } = string.Empty;
    public string TenantName { get; } = string.Empty;
    public int UserId { get; }
    public string UserName { get; } = string.Empty;
    public int? EmployeeId { get; }
    public IReadOnlyCollection<string> Roles { get; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Permissions { get; } = Array.Empty<string>();
    public bool IsResolved { get; }

    public bool Has(string permission) => _permissions.Contains(permission);

    private string? Read(string type) => _principal?.FindFirst(type)?.Value;

    private int ReadInt(string type) => int.TryParse(Read(type), out var value) ? value : 0;

    private int? ReadNullableInt(string type) => int.TryParse(Read(type), out var value) ? value : null;
}
