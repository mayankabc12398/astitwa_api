using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HrSuite.API.Auth;

public sealed class JwtTokenIssuer : IAuthTokenIssuer
{
    private readonly JwtOptions _options;

    public JwtTokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is missing or shorter than 32 characters. Set it via user-secrets " +
                "or the HRSUITE_Jwt__SigningKey environment variable.");
        }
    }

    public (string Token, DateTime ExpiresOn) Issue(AuthenticatedUser user)
    {
        var expiresOn = DateTime.UtcNow.AddMinutes(_options.LifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(HrClaims.TenantId, user.TenantId.ToString()),
            new(HrClaims.TenantCode, user.TenantCode),
            new(HrClaims.TenantName, user.TenantName)
        };

        if (user.EmployeeId is int employeeId)
            claims.Add(new Claim(HrClaims.EmployeeId, employeeId.ToString()));

        foreach (var role in user.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim(ClaimTypes.Role, role));

        foreach (var permission in user.Permissions.Distinct(StringComparer.OrdinalIgnoreCase))
            claims.Add(new Claim(HrClaims.Permission, permission));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresOn,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresOn);
    }
}
