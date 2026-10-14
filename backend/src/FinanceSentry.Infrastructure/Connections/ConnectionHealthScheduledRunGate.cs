namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using Microsoft.Extensions.Logging;

/// <summary>
/// The pause of a scheduled sync on a credential the connection-health policy has confirmed lost, as the bank
/// scheduler already parks <c>reauth_required</c> accounts. The credential is skipped until its daily probe is
/// due (<see cref="ConnectionHealthPolicy.ScheduledRunPausedUntil"/>); that probe is an ordinary run, so a
/// success clears ActionRequired through the policy's own <c>RecordSuccess</c>. Only a scheduled job asks:
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
        if (ConnectionHealthPolicy.ScheduledRunPausedUntil(health, now, options) is not { } nextProbeAt)
            return false;

        logger.LogInformation(
            "Skipping the scheduled {Provider} sync for user {UserId}: the connection needs the user to reconnect; "
            + "next probe at {NextProbeAt:O}",
            provider,
            userId,
            nextProbeAt);
        return true;
    }
}
