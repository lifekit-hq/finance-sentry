namespace FinanceSentry.Infrastructure.Connections;

using FinanceSentry.Core.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Shadow mode of the connection-health policy (Option B, S1). Runs the policy on each reported attempt,
/// stores the resulting health, and logs one structured line carrying the outcome the policy would take,
/// so a week of production can be compared with the alerts that actually fired. Changes no alert.
/// </summary>
public sealed class ConnectionHealthShadowRecorder(
    IOptionsMonitor<ConnectionHealthOptions> options,
    TimeProvider time,
    ILogger<ConnectionHealthShadowRecorder> logger) : IConnectionHealthShadow
{
    public Task<ConnectionHealthEvaluation?> RecordSuccessAsync(
        ConnectionHealthSubject subject,
        ConnectionHealth current,
        Func<ConnectionHealth, CancellationToken, Task> persist,
        CancellationToken ct = default) =>
        RecordAsync(subject, null, () => ConnectionHealthPolicy.RecordSuccess(current, time.GetUtcNow()), persist, ct);

    public Task<ConnectionHealthEvaluation?> RecordFailureAsync(
        ConnectionHealthSubject subject,
        ConnectionHealth current,
        ProviderFailure failure,
        Func<ConnectionHealth, CancellationToken, Task> persist,
        CancellationToken ct = default) =>
        RecordAsync(
            subject,
            failure,
            () => ConnectionHealthPolicy.RecordFailure(current, failure, time.GetUtcNow(), options.CurrentValue),
            persist,
            ct);

    private async Task<ConnectionHealthEvaluation?> RecordAsync(
        ConnectionHealthSubject subject,
        ProviderFailure? failure,
        Func<ConnectionHealthEvaluation> evaluate,
        Func<ConnectionHealth, CancellationToken, Task> persist,
        CancellationToken ct)
    {
        try
        {
            var evaluation = evaluate();

            logger.LogInformation(
                "Connection health (shadow): {Provider} {SubjectKind} {SubjectId} of user {UserId} went "
                + "{PreviousHealthState} -> {HealthState}, would {HealthOutcome}; attempt {Attempt} "
                + "{FailureClass}/{FailureStrength} {FailureCode}, {ConsecutiveFailures} consecutive failures",
                subject.Provider,
                subject.Kind,
                subject.Id,
                subject.UserId,
                evaluation.Previous.State,
                evaluation.Health.State,
                evaluation.Outcome,
                failure is null ? "success" : "failure",
                failure?.Class,
                failure?.Strength,
                failure?.Code,
                evaluation.Health.ConsecutiveFailures);

            if (evaluation.Health != evaluation.Previous)
                await persist(evaluation.Health, ct);

            return evaluation;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Connection health (shadow): could not record the attempt for {Provider} {SubjectKind} {SubjectId}",
                subject.Provider,
                subject.Kind,
                subject.Id);
            return null;
        }
    }
}
