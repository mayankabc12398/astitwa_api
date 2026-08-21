using System.Diagnostics.CodeAnalysis;

namespace HrSuite.Integrations.Email;

/// <summary>
/// Deserialised from sys_tenant_integration.settings_json. Every field is per tenant, so
/// two clients can point at entirely different mail servers with no code change.
/// </summary>
public sealed class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }

    /// <summary>
    /// True when this tenant's row carries enough to attempt delivery. The attribute states
    /// the invariant to the compiler as well as the reader: once IsUsable is true, Host and
    /// FromAddress are non-null, so the caller neither re-checks them nor suppresses a
    /// warning it cannot actually justify.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Host), nameof(FromAddress))]
    public bool IsUsable => !string.IsNullOrWhiteSpace(Host)
                            && Port > 0
                            && !string.IsNullOrWhiteSpace(FromAddress);
}
