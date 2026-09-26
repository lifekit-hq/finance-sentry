namespace FinanceSentry.Modules.Companion.Infrastructure.Jobs;

using FinanceSentry.Modules.Companion.Application.Services;
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
/// events ⇒ no wake (no forced empty message).
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 120)]
public sealed class CompanionDigestJob(
    INotificationSettingRepository settings,
    ICompanionEventRepository events,
    IAgentWakeDispatcher dispatcher,
    ILogger<CompanionDigestJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var userIds = await events.ListHeldForDigestUserIdsAsync(ct);

        foreach (var userId in userIds)
        {
            var setting = await settings.GetOrDefaultAsync(userId, ct);
            if (QuietHours.LocalHour(setting.TimeZoneId, now) != setting.DigestHourLocal)
            {
                continue;
            }

            var held = await events.ListHeldForDigestAsync(userId, ct);
            if (held.Count == 0)
            {
                continue;
            }

            await dispatcher.WakeDigestAsync(userId, held.Count, ct);
            logger.LogInformation("Companion digest wake for {User}: {Count} held events", userId, held.Count);
        }
    }
}
