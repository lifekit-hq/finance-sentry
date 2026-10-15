namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using Microsoft.Extensions.Logging;

/// <summary>
/// The pause of a scheduled sync on a credential the connection-health policy has confirmed lost (as the bank
/// scheduler already parks <c>reauth_required</c> accounts) or that the provider asked to leave alone for a while
/// (<see cref="ConnectionHealth.NotBefore"/>). The credential is skipped until its daily probe is due or the named
/// time has come (<see cref="ConnectionHealthPolicy.ScheduledRunPausedUntil"/>); that run is an ordinary one, so a
/// success clears the state through the policy's own <c>RecordSuccess</c>. Only a scheduled job asks:
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
        if (ConnectionHealthPolicy.ScheduledRunPausedUntil(health, now, options) is not { } pausedUntil)
            return false;

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
}
