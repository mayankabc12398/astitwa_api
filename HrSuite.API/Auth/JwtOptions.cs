namespace HrSuite.API.Auth;

/// <summary>
/// Signing key comes from user-secrets or the environment. Section 11: no secrets in source.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "HrSuite";
    public string Audience { get; set; } = "HrSuite.Web";
    public string SigningKey { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 480;
}
