namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using Microsoft.Extensions.Logging;

/// <summary>
/// The early re-checks of a suspect credential failure (the resilience contract's suspect row, finance-framework §5.7):
/// the attempt that raised the suspicion is followed by one quick look, and a quick look that leaves it standing by the
/// confirming look once the confirmation window has passed (<see cref="ConnectionHealthPolicy.SuspectRecheckAfter"/>).
/// Each look is an ordinary run of the source's own sync for that one connection, scheduled with Hangfire's
/// <c>BackgroundJob.Schedule</c> like a source's <see cref="ConnectionHealthSourceOptions.EarlyRechecks"/>, and goes
/// through the same gate. A look runs only if nothing has tried the connection since it was scheduled, so when several
/// accounts of one connection (a Monobank token's cards) each schedule one, only the last makes a call. Only a provider in <see cref="ConnectionHealthOptions.ActiveProviders"/> gets them; for a shadow
/// provider one line says when the look would have run.
/// </summary>
public static class ConnectionHealthSuspectRecheck
{
    /// <summary>
    /// After an attempt on a connection: schedules its next look through <paramref name="schedule"/> when one is due
    /// and the provider is active. Best-effort: a failure to schedule costs only that look.
    /// </summary>
    /// <param name="before">The connection's health before the attempt.</param>
    /// <param name="after">Its health after the attempt was recorded.</param>
    /// <param name="recheck">How many looks have run, the attempt just made included when it was one (0 for a scheduled run).</param>
    /// <param name="schedule">
    /// Schedules look number <c>recheck + 1</c> after the given delay, handing it <paramref name="now"/> as the time it
    /// was scheduled (for <see cref="IsDue"/>).
    /// </param>
    /// <param name="minDelay">The provider's own floor between calls on one credential (Monobank: one a minute).</param>
    public static void ScheduleIfDue(
        ILogger logger,
        ConnectionHealthSubject subject,
        ConnectionHealth before,
        ConnectionHealth after,
        int recheck,
        DateTimeOffset now,
        ConnectionHealthOptions options,
        Action<TimeSpan, DateTimeOffset> schedule,
        TimeSpan minDelay = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(schedule);

        if (ConnectionHealthPolicy.SuspectRecheckAfter(before, after, recheck, now, options) is not { } delay)
            return;

        if (delay < minDelay)
            delay = minDelay;

        if (!options.IsActive(subject.Provider))
        {
            logger.LogInformation(
                "Connection health (shadow): {Provider} {SubjectKind} {SubjectId} of user {UserId} has a suspect credential "
                + "failure since {SuspectSince:O}; as an active provider it would be looked at again in {Delay} "
                + "(look {Recheck}), and no call is made",
                subject.Provider,
                subject.Kind,
                subject.Id,
                subject.UserId,
                after.SuspectSince,
                delay,
                recheck + 1);
            return;
        }

        try
        {
            schedule(delay, now);
            logger.LogInformation(
                "Connection health: {Provider} {SubjectKind} {SubjectId} of user {UserId} has a suspect credential failure "
                + "since {SuspectSince:O}; looking again in {Delay} (look {Recheck})",
                subject.Provider,
                subject.Kind,
                subject.Id,
                subject.UserId,
                after.SuspectSince,
                delay,
                recheck + 1);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Connection health: could not schedule the next look at {Provider} {SubjectKind} {SubjectId}",
                subject.Provider, subject.Kind, subject.Id);
        }
    }

    /// <summary>
    /// Whether a scheduled look still has work when it runs: the provider is still active, the suspicion is neither
    /// confirmed (ActionRequired), cleared by a success, nor overtaken by Dormant's slowed cadence, and no attempt on the
    /// connection was recorded after <paramref name="scheduledAt"/> (another look or a scheduled run already made it).
    /// </summary>
    public static bool IsDue(string provider, ConnectionHealth health, ConnectionHealthOptions options, DateTimeOffset scheduledAt)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(options);

        return options.IsActive(provider)
            && health.SuspectSince is not null
            && health.State is not (ConnectionHealthState.ActionRequired or ConnectionHealthState.Dormant)
            && !(health.LastFailureAt > scheduledAt);
    }
}
