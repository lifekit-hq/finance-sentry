namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// The in-app sync-failure alert of a provider in <see cref="ConnectionHealthOptions.ActiveProviders"/> (Option B):
/// the policy's evaluation decides when the alert is raised and resolved, instead of the path's own streak.
/// <list type="bullet">
/// <item><see cref="ConnectionHealthOutcome.NotifyActionRequired"/> raises it with the reconnect copy
/// (<see cref="SyncFailureClass.Credential"/>), superseding a Failing alert still open, and no silence window
/// holds it back: a confirmed credential loss is always told.</item>
/// <item><see cref="ConnectionHealthOutcome.NotifyFailing"/> raises it with the outage copy when the last
/// failure was transient, else the neutral one.</item>
/// <item>A success after any failure resolves it, and the Dormant notice when the connection was Dormant.</item>
/// <item><see cref="ConnectionHealthOutcome.GoDormant"/> raises the one notice that the connection may be closed
/// (info, once per connection ever); the Failing alert stays open.</item>
/// </list>
/// The user's <see cref="UserAlertPreferences.SyncFailureAlerts"/> gates all of them. Best-effort: a failure here is
/// logged and never reaches the sync.
/// </summary>
public static class ConnectionHealthSyncFailureAlerts
{
    /// <summary>Raises the alert when this failure just made the connection Failing, ActionRequired or Dormant.</summary>
    public static async Task RaiseIfDueAsync(
        IAlertGeneratorService alerts,
        IUserAlertPreferencesReader preferences,
        ILogger logger,
        Guid userId,
        string provider,
        ConnectionHealthEvaluation? evaluation,
        string? errorCode)
    {
        if (evaluation is { Outcome: ConnectionHealthOutcome.GoDormant })
        {
            await RaiseDormantAsync(alerts, preferences, logger, userId, provider, evaluation.Health);
            return;
        }

        SyncFailureClass? failureClass = evaluation?.Outcome switch
        {
            ConnectionHealthOutcome.NotifyActionRequired => SyncFailureClass.Credential,
            ConnectionHealthOutcome.NotifyFailing => evaluation.Health.LastFailureClass is { } lastClass && lastClass.IsTransientLike()
                ? SyncFailureClass.Outage
                : SyncFailureClass.Unknown,
            _ => null,
        };

        if (failureClass is not { } raised)
            return;

        try
        {
            var prefs = await preferences.GetAsync(userId);
            if (prefs is null || !prefs.SyncFailureAlerts) return;
            if (raised == SyncFailureClass.Credential)
                await alerts.EscalateSyncFailureAlertAsync(userId, provider, null, null, errorCode);
            else
                await alerts.GenerateFailingSyncFailureAlertAsync(userId, provider, null, null, errorCode, raised);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not raise the {Provider} sync-failure alert for user {UserId}", provider, userId);
        }
    }

    /// <summary>
    /// Resolves the alert after a success that ended a failure streak: on any success not preceded by a
    /// Healthy state, not only on <see cref="ConnectionHealthOutcome.Resolve"/>, so an alert raised before the
    /// policy drove this path, on a connection the policy held only Degraded, is cleared as well. A success
    /// the policy could not record (null) resolves too.
    /// </summary>
    public static async Task ResolveIfRecoveredAsync(
        IAlertGeneratorService alerts,
        IUserAlertPreferencesReader preferences,
        ILogger logger,
        Guid userId,
        string provider,
        ConnectionHealthEvaluation? evaluation)
    {
        if (evaluation is { Previous.State: ConnectionHealthState.Healthy })
            return;

        try
        {
            var prefs = await preferences.GetAsync(userId);
            if (prefs is null || !prefs.SyncFailureAlerts) return;
            await alerts.ResolveSyncFailureAlertAsync(userId, provider, null);
            if (evaluation is { Previous.State: ConnectionHealthState.Dormant, Health.State: not ConnectionHealthState.Dormant })
                await alerts.ResolveConnectionDormantAlertAsync(userId, provider, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve the {Provider} sync-failure alert for user {UserId}", provider, userId);
        }
    }

    private static async Task RaiseDormantAsync(
        IAlertGeneratorService alerts,
        IUserAlertPreferencesReader preferences,
        ILogger logger,
        Guid userId,
        string provider,
        ConnectionHealth health)
    {
        try
        {
            var prefs = await preferences.GetAsync(userId);
            if (prefs is null || !prefs.SyncFailureAlerts) return;
            await alerts.GenerateConnectionDormantAlertAsync(
                userId, provider, null, null, FailingFor(health), othersSyncing: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not raise the {Provider} Dormant notice for user {UserId}", provider, userId);
        }
    }

    /// <summary>How long a connection has been failing, by its streak: what the Dormant notice tells.</summary>
    public static TimeSpan FailingFor(ConnectionHealth health) =>
        health is { FirstFailureAt: { } first, LastFailureAt: { } last } ? last - first : TimeSpan.Zero;
}
