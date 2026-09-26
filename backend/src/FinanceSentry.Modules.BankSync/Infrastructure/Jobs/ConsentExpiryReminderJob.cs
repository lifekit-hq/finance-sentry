namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily detector that nudges the user to reconnect a TrueLayer bank connection <b>before</b> its
/// open-banking consent (~90 days) expires, instead of after data silently goes stale. Raises a
/// <c>ConsentExpiring</c> alert per connection inside the reminder window; the Alerts → Companion
/// pipeline delivers it (Telegram). Dedup/silence lives in the alert generator so running daily is
/// safe.
/// </summary>
public class ConsentExpiryReminderJob(
    ITrueLayerConnectionRepository connections,
    IAlertGeneratorService alerts,
    ILogger<ConsentExpiryReminderJob> logger)
{
    /// <summary>Days before expiry to start reminding.</summary>
    public const int ReminderWindowDays = 7;

    private readonly ITrueLayerConnectionRepository _connections = connections;
    private readonly IAlertGeneratorService _alerts = alerts;
    private readonly ILogger<ConsentExpiryReminderJob> _logger = logger;

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.AddDays(ReminderWindowDays);
        var expiring = await _connections.GetLinkedExpiringBeforeAsync(threshold, ct);
        var expiringIds = expiring.Select(c => c.Id).ToHashSet();

        var reminded = 0;
        foreach (var c in expiring)
        {
            if (c.ConnectionExpiresAt is null)
                continue;

            await _alerts.GenerateConsentExpiringAlertAsync(
                c.UserId, c.Id, c.ProviderDisplayName, c.ConnectionExpiresAt.Value, ct);
            reminded++;
        }

        if (reminded > 0)
            _logger.LogInformation(
                "Consent-expiry reminder raised alerts for {Count} connection(s) expiring within {Days} days.",
                reminded, ReminderWindowDays);

        var allLinked = await _connections.GetAllLinkedAsync(ct);
        foreach (var c in allLinked)
        {
            if (expiringIds.Contains(c.Id))
                continue;

            await _alerts.ResolveConsentExpiringAlertAsync(c.UserId, c.Id, ct);
        }
    }
}
