namespace FinanceSentry.Modules.Companion.Infrastructure.Jobs;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily digest trigger (feature 031, US3; issue #686). Runs hourly; for every user who currently has
/// at least one event held for the digest, wakes the agent once their local hour matches their
/// configured digest hour, to compose the consolidated roll-up. Gated on held events existing, not on
/// the user's proactivity mode — <see cref="Application.Services.MaterialityPolicy"/> holds some kinds
/// (e.g. sync failures) for the digest regardless of mode, so a mode-only gate stranded them
/// (issue #686). The agent pulls the held events, delivers, and acks (so they don't repeat). No held
/// events ⇒ no wake (no forced empty message). MarketStructure and NewsCluster events do not count: the
/// ledger-pulse drains them, so a pool holding only those does not earn a Digest session.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 120)]
public sealed class CompanionDigestJob(
    INotificationSettingRepository settings,
    ICompanionEventRepository events,
    ISyncFailureReconciler reconciler,
    IAgentWakeDispatcher dispatcher,
    ILogger<CompanionDigestJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var userIds = await events.ListHeldForDigestUserIdsUnscopedAsync(ct);

        foreach (var userId in userIds)
        {
            var setting = await settings.GetOrDefaultUnscopedAsync(userId, ct);
            if (QuietHours.LocalHour(setting.TimeZoneId, now) != setting.DigestHourLocal)
            {
                continue;
            }

            // Resolved failures expire here and long-open ones escalate out of the digest, so only live held events count.
            var held = (await reconciler.ReconcileAsync(await events.ListHeldForDigestUnscopedAsync(userId, ct), ct))
                .Where(e => e.Disposition == EventDisposition.HeldForDigest && !IsPulseBatched(e.Kind))
                .ToList();
            if (held.Count == 0)
            {
                continue;
            }

            await dispatcher.WakeDigestAsync(userId, held.Count, ct);
            logger.LogInformation("Companion digest wake for {User}: {Count} held events", userId, held.Count);
        }
    }

    // The ledger-pulse drains these two kinds three times a day, so they alone never justify a daily Digest session.
    private static bool IsPulseBatched(CompanionEventKind kind)
        => kind is CompanionEventKind.MarketStructure or CompanionEventKind.NewsCluster;
}
