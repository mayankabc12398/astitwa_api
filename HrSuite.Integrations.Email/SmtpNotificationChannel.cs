using System.Text.Json;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Modularity;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace HrSuite.Integrations.Email;

/// <summary>
/// Layer 4 adapter behind INotificationChannel (section 9).
///
/// Core never names this type. It is discovered at startup, matched to the tenant's
/// sys_tenant_integration row by IntegrationKey, and asked to send. Every failure mode —
/// not configured, misconfigured, server unreachable — comes back as a NotificationResult,
/// never as an exception, so leave approval succeeds regardless.
/// </summary>
public sealed class SmtpNotificationChannel : INotificationChannel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ITenantIntegrationService _integrations;
    private readonly ILogger<SmtpNotificationChannel> _log;

    public SmtpNotificationChannel(ITenantIntegrationService integrations, ILogger<SmtpNotificationChannel> log)
    {
        _integrations = integrations;
        _log = log;
    }

    public string IntegrationKey => EmailIntegrationModule.Key;

    public async Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        try
        {
            var integration = await _integrations.GetAsync(IntegrationKey, ct).ConfigureAwait(false);
            if (integration is null || !integration.IsEnabled) return NotificationResult.NotConfigured();

            var settings = Parse(integration.SettingsJson);
            if (settings is null || !settings.IsUsable) return NotificationResult.NotConfigured();

            if (string.IsNullOrWhiteSpace(message.To)) return NotificationResult.NotConfigured();

            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(settings.FromName ?? settings.FromAddress, settings.FromAddress));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new TextPart("plain") { Text = message.Body };

            using var client = new SmtpClient();
            await client.ConnectAsync(
                settings.Host,
                settings.Port,
                settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(settings.UserName))
                await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, ct).ConfigureAwait(false);

            await client.SendAsync(mime, ct).ConfigureAwait(false);
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);

            return NotificationResult.Sent();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SMTP delivery to {To} failed.", message.To);
            return NotificationResult.Failed("The message could not be delivered.");
        }
    }

    private static SmtpSettings? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SmtpSettings>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
