using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Identity;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Identity;

/// <summary>
/// Login-time reads. Derives from <see cref="UnscopedRepositoryBase"/> because there is no
/// tenant context yet — the tenant is being established by this very call.
/// </summary>
public sealed class UserAuthRepository : UnscopedRepositoryBase, IUserAuthRepository
{
    public UserAuthRepository(IDbConnectionFactory factory) : base(factory) { }

    public Task<UserCredential?> FindForLoginAsync(string tenantCode, string userName, CancellationToken ct = default)
        => QuerySingleAsync<UserCredential>(
            "sp_sys_user_get_for_login",
            Args().Set("tenant_code", tenantCode).Set("user_name", userName),
            ct);

    public async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> GetRolesAndPermissionsAsync(
        int tenantId, int userId, CancellationToken ct = default)
    {
        var (roles, permissions) = await QueryTwoAsync<string, string>(
            "sp_sys_user_permissions",
            Args().Set("tenant_id", tenantId).Set("user_id", userId),
            ct).ConfigureAwait(false);

        return (roles, permissions);
    }

    public Task TouchLoginAsync(int tenantId, int userId, CancellationToken ct = default)
        => ExecuteAsync(
            "sp_sys_user_touch_login",
            Args().Set("tenant_id", tenantId).Set("user_id", userId),
            ct);
}
