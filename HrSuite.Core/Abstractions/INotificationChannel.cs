namespace HrSuite.Core.Abstractions;

/// <summary>
/// Layer 4 contract. Core calls this and never learns which adapter (if any) is behind it.
/// Implementations must be no-ops when the tenant has no integration enabled — never throw.
/// </summary>
public interface INotificationChannel
{
    string IntegrationKey { get; }
    Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken ct = default);
}

public sealed record NotificationMessage(
    string To,
    string Subject,
    string Body,
    string? TemplateKey = null,
    IReadOnlyDictionary<string, object?>? Tokens = null);

public sealed record NotificationResult(bool Delivered, bool Skipped, string? Reason = null)
{
    public static NotificationResult Sent() => new(true, false);
    public static NotificationResult NotConfigured() => new(false, true, "No notification integration enabled for this tenant.");
    public static NotificationResult Failed(string reason) => new(false, false, reason);
}

/// <summary>
/// Resolves the enabled channel for the current tenant. Returns a no-op when nothing is configured,
/// so business flows such as leave approval never depend on an integration being present.
/// </summary>
public interface INotificationDispatcher
{
    Task<NotificationResult> DispatchAsync(NotificationMessage message, CancellationToken ct = default);
}
