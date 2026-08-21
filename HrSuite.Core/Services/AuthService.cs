using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace HrSuite.Core.Services;

/// <summary>
/// Layer 1 login rules, true for every customer. Nothing here knows a client name.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserAuthRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IAuthTokenIssuer _tokens;
    private readonly ILogger<AuthService> _log;

    public AuthService(
        IUserAuthRepository users,
        IPasswordHasher hasher,
        IAuthTokenIssuer tokens,
        ILogger<AuthService> log)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
        _log = log;
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TenantCode) ||
            string.IsNullOrWhiteSpace(request.UserName) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<LoginResponse>.Invalid("Tenant, user name and password are all required.");
        }

        var credential = await _users
            .FindForLoginAsync(request.TenantCode.Trim(), request.UserName.Trim(), ct)
            .ConfigureAwait(false);

        // One message for every failure mode: a wrong tenant, a wrong user and a wrong
        // password must be indistinguishable to the caller.
        if (credential is null || !credential.IsActive || !_hasher.Verify(request.Password, credential.PasswordHash))
        {
            _log.LogInformation("Failed login for {UserName} on tenant {TenantCode}.", request.UserName, request.TenantCode);
            return Result<LoginResponse>.Fail(ErrorCode.Unauthorized, "Sign-in failed. Check the tenant, user name and password.");
        }

        var (roles, permissions) = await _users
            .GetRolesAndPermissionsAsync(credential.TenantId, credential.UserId, ct)
            .ConfigureAwait(false);

        var user = new AuthenticatedUser
        {
            UserId = credential.UserId,
            TenantId = credential.TenantId,
            TenantCode = credential.TenantCode,
            TenantName = credential.TenantName,
            UserName = credential.UserName,
            DisplayName = credential.DisplayName,
            Email = credential.Email,
            EmployeeId = credential.EmployeeId,
            Roles = roles,
            Permissions = permissions
        };

        var (token, expiresOn) = _tokens.Issue(user);

        await _users.TouchLoginAsync(credential.TenantId, credential.UserId, ct).ConfigureAwait(false);

        return Result<LoginResponse>.Success(new LoginResponse
        {
            Token = token,
            ExpiresOn = expiresOn,
            User = user
        });
    }
}
