using HrSuite.Common.Results;
using HrSuite.Core.Domain.Identity;

namespace HrSuite.Core.Abstractions;

/// <summary>Password hashing. Implemented outside Core so the algorithm can change without touching rules.</summary>
public interface IPasswordHasher
{
    string Hash(string plainText);
    bool Verify(string plainText, string hash);
}

/// <summary>Turns an authenticated user into a bearer token. Implemented by the API host.</summary>
public interface IAuthTokenIssuer
{
    (string Token, DateTime ExpiresOn) Issue(AuthenticatedUser user);
}

/// <summary>
/// Login-time reads. These run before a tenant context exists, so the tenant is resolved
/// from the supplied tenant code rather than from ambient state.
/// </summary>
public interface IUserAuthRepository
{
    Task<UserCredential?> FindForLoginAsync(string tenantCode, string userName, CancellationToken ct = default);
    Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> GetRolesAndPermissionsAsync(
        int tenantId, int userId, CancellationToken ct = default);
    Task TouchLoginAsync(int tenantId, int userId, CancellationToken ct = default);
}

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
}
