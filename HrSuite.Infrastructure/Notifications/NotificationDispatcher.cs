using HrSuite.Core.Abstractions;
using HrSuite.Core.Modularity;
using Microsoft.Extensions.Logging;

namespace HrSuite.Infrastructure.Notifications;

/// <summary>
/// Layer 1 side of the integration boundary (section 9).
///
/// Channels are contributed by Layer 4 assemblies through DI. This class matches the
/// registered channels against the tenant's enabled integrations. If nothing matches — no
/// adapter deployed, or the tenant has it switched off — the send is skipped and reported as
/// skipped. It is never an exception, so leave approval works with no email configured.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ITenantIntegrationService _integrations;
    private readonly ILogger<NotificationDispatcher> _log;

    public NotificationDispatcher(
        IEnumerable<INotificationChannel> channels,
        ITenantIntegrationService integrations,
        ILogger<NotificationDispatcher> log)
    {
        _channels = channels;
        _integrations = integrations;
        _log = log;
    }

    public async Task<NotificationResult> DispatchAsync(NotificationMessage message, CancellationToken ct = default)
    {
        try
        {
            var channels = _channels.ToList();
            if (channels.Count == 0) return NotificationResult.NotConfigured();

            var enabled = await _integrations.GetEnabledAsync(ct).ConfigureAwait(false);
            var enabledKeys = enabled.Select(e => e.IntegrationKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var channel = channels.FirstOrDefault(c => enabledKeys.Contains(c.IntegrationKey));
            if (channel is null)
            {
                _log.LogDebug("No enabled notification integration for this tenant. Message to {To} skipped.", message.To);
                return NotificationResult.NotConfigured();
            }

            return await channel.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // An integration failure is never allowed to fail the business operation that
            // triggered it. Record it and carry on.
            _log.LogError(ex, "Notification dispatch failed for {To}.", message.To);
            return NotificationResult.Failed("Notification could not be delivered.");
        }
    }
}
