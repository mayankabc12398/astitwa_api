namespace HrSuite.Core.Domain.Identity;

public sealed class LoginRequest
{
    public string TenantCode { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>The row behind a login attempt. The hash never leaves the auth service.</summary>
public sealed class UserCredential
{
    public int UserId { get; set; }
    public int TenantId { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public int? EmployeeId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AuthenticatedUser
{
    public int UserId { get; init; }
    public int TenantId { get; init; }
    public string TenantCode { get; init; } = string.Empty;
    public string TenantName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public int? EmployeeId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
}

public sealed class LoginResponse
{
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiresOn { get; init; }
    public AuthenticatedUser User { get; init; } = new();
}
