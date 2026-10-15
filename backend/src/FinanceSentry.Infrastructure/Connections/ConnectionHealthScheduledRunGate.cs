namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using Microsoft.Extensions.Logging;

/// <summary>
/// The pause of a scheduled sync on a credential the connection-health policy has confirmed lost (as the bank
/// scheduler already parks <c>reauth_required</c> accounts) or that the provider asked to leave alone for a while
/// (<see cref="ConnectionHealth.NotBefore"/>), and, for a provider in <see cref="ConnectionHealthOptions.ActiveProviders"/>,
/// the slowed cadence of a Dormant one. The credential is skipped until its daily probe is due or the named
/// time has come (<see cref="ConnectionHealthPolicy.ScheduledRunPausedUntil"/>); that run is an ordinary one, so a
/// success clears the state through the policy's own <c>RecordSuccess</c>. A shadow provider's Dormant credential runs
/// as before, and one line says when the slowed cadence would have run it. Only a scheduled job asks:
/// a sync the person triggers never goes through this gate.
/// </summary>
public static class ConnectionHealthScheduledRunGate
{
    /// <summary>True, after logging the skip, when the scheduled run must leave this credential alone.</summary>
    public static bool ShouldSkip(
        ILogger logger,
        string provider,
        Guid userId,
        ConnectionHealth health,
        DateTimeOffset now,
        ConnectionHealthOptions options)
    {
        var active = options.IsActive(provider);
        if (ConnectionHealthPolicy.ScheduledRunPausedUntil(health, now, options, slowDormant: active) is not { } pausedUntil)
        {
            if (!active && ConnectionHealthPolicy.DormantPausedUntil(health, now, options) is { } wouldPauseUntil)
            {
                logger.LogInformation(
                    "Connection health (shadow): the {Provider} connection of user {UserId} is Dormant; as an active "
                    + "provider its scheduled run would wait until {PausedUntil:O}, and it runs as before",
                    provider,
                    userId,
                    wouldPauseUntil);
            }

            return false;
        }

        logger.LogInformation(
            "Skipping the scheduled {Provider} sync for user {UserId}: the connection is paused ({HealthState}, "
            + "last failure {FailureClass}); next run at {PausedUntil:O}",
            provider,
            userId,
            health.State,
            health.LastFailureClass,
            pausedUntil);
        return true;
    }

    /// <summary>
    /// The Dormant cadence alone, for a scheduler that applies the other pauses its own way (the bank scheduler parks
    /// <c>reauth_required</c> accounts and reads <see cref="ConnectionHealth.NotBefore"/> itself): when the next run may
    /// try <paramref name="subject"/>, or null when it may go ahead. A shadow provider is never paused; what the cadence
    /// would have done is logged.
    /// </summary>
    public static DateTimeOffset? DormantPausedUntil(
        ILogger logger,
        ConnectionHealthSubject subject,
        ConnectionHealth health,
        DateTimeOffset now,
        ConnectionHealthOptions options)
    {
        if (ConnectionHealthPolicy.DormantPausedUntil(health, now, options) is not { } pausedUntil)
            return null;

        if (options.IsActive(subject.Provider))
            return pausedUntil;

        logger.LogInformation(
            "Connection health (shadow): {Provider} {SubjectKind} {SubjectId} of user {UserId} is Dormant; as an active "
            + "provider its scheduled run would wait until {PausedUntil:O}, and it runs as before",
            subject.Provider,
            subject.Kind,
            subject.Id,
            subject.UserId,
            pausedUntil);
        return null;
    }
}
