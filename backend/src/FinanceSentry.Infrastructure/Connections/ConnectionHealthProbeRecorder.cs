namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using Microsoft.Extensions.Logging;

/// <summary>
/// Makes one probe call (<see cref="IConnectionHealthProbe"/>) and records its result for a provider whose alerts the
/// policy drives: a success as any success, resolving the sync-failure alert when the connection recovers; a failure
/// through <see cref="IConnectionHealthShadow.RecordProbeFailureAsync"/>, raising the alert the evaluation earns (the
/// reconnect ask when the probe confirms a suspect credential failure). Best-effort: never throws for the provider's answer.
/// </summary>
public sealed class ConnectionHealthProbeRecorder(
    IConnectionHealthShadow connectionHealth,
    IAlertGeneratorService alerts,
    IUserAlertPreferencesReader userPreferences,
    TimeProvider time,
    ILogger<ConnectionHealthProbeRecorder> logger)
{
    /// <param name="subject">The connection probed.</param>
    /// <param name="current">Its stored health.</param>
    /// <param name="call">The provider's cheap authenticated call.</param>
    /// <param name="classifier">Reads a failed call as the provider's sync does.</param>
    /// <param name="persist">Stores the new health on the connection's row.</param>
    /// <param name="ct">Cancels the call; the result is recorded regardless.</param>
    /// <returns>The evaluation, or null if it could not be recorded.</returns>
    public async Task<ConnectionHealthEvaluation?> ProbeAsync(
        ConnectionHealthSubject subject,
        ConnectionHealth current,
        Func<CancellationToken, Task> call,
        IProviderFailureClassifier classifier,
        Func<ConnectionHealth, CancellationToken, Task> persist,
        CancellationToken ct = default)
    {
        try
        {
            await call(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Connection health probe: {Provider} {SubjectKind} {SubjectId} of user {UserId} failed its probe",
                subject.Provider,
                subject.Kind,
                subject.Id,
                subject.UserId);

            var failure = classifier.Classify(ex, new ProviderFailureContext(time.GetUtcNow(), current));
            var failed = await connectionHealth.RecordProbeFailureAsync(subject, current, failure, persist, CancellationToken.None);
            await ConnectionHealthSyncFailureAlerts.RaiseIfDueAsync(
                alerts, userPreferences, logger, subject.UserId, subject.Provider, failed, ex.GetType().Name);
            return failed;
        }

        var succeeded = await connectionHealth.RecordSuccessAsync(subject, current, persist, CancellationToken.None);
        await ConnectionHealthSyncFailureAlerts.ResolveIfRecoveredAsync(
            alerts, userPreferences, logger, subject.UserId, subject.Provider, succeeded);
        return succeeded;
    }
}
