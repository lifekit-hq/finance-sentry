namespace FinanceSentry.Core.Connections;

/// <summary>
/// The connection-health state machine (report §6–§7). Pure: it reads the current health and one attempt,
/// and returns the next health and the outcome, which fires only on the transition that earns it.
/// <list type="bullet">
/// <item>One success makes any state Healthy; it resolves only a state the user was told about.</item>
/// <item>A definitive credential failure, or a suspect one seen again after
/// <see cref="ConnectionHealthOptions.SuspectConfirmationWindow"/>, makes any state ActionRequired, which holds until a success.</item>
/// <item>Any other counted failure makes Healthy Degraded (silent); Failing once there are
/// <see cref="ConnectionHealthOptions.FailingMinConsecutiveFailures"/> failures and
/// <see cref="ConnectionHealthOptions.FailingMinTimeWithoutSuccess"/> without a success; Dormant once the streak
/// has lasted <see cref="ConnectionHealthOptions.DormantAfter"/>.</item>
/// <item>An internal failure is ours, not the provider's: it leaves the health as it was.</item>
/// </list>
/// </summary>
public static class ConnectionHealthPolicy
{
    public static ConnectionHealthEvaluation RecordSuccess(ConnectionHealth current, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(current);

        var next = current with
        {
            State = ConnectionHealthState.Healthy,
            ConsecutiveFailures = 0,
            FirstFailureAt = null,
            LastSuccessAt = at,
            SuspectSince = null,
            StateChangedAt = current.State == ConnectionHealthState.Healthy ? current.StateChangedAt : at,
        };

        var userWasTold = current.State is ConnectionHealthState.Failing
            or ConnectionHealthState.ActionRequired
            or ConnectionHealthState.Dormant;

        return new ConnectionHealthEvaluation(current, next, userWasTold ? ConnectionHealthOutcome.Resolve : ConnectionHealthOutcome.None);
    }

    public static ConnectionHealthEvaluation RecordFailure(
        ConnectionHealth current, ProviderFailure failure, DateTimeOffset at, ConnectionHealthOptions options)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(options);

        if (failure.Class == FailureClass.Internal)
            return new ConnectionHealthEvaluation(current, current, ConnectionHealthOutcome.None);

        var counted = current with
        {
            ConsecutiveFailures = current.ConsecutiveFailures + 1,
            FirstFailureAt = current.FirstFailureAt ?? at,
            LastFailureAt = at,
            LastFailureClass = failure.Class,
            LastFailureCode = Truncate(failure.Code),
        };

        if (current.State == ConnectionHealthState.ActionRequired)
            return new ConnectionHealthEvaluation(current, counted, ConnectionHealthOutcome.None);

        if (failure.Class == FailureClass.Credential)
        {
            var confirmed = failure.Strength == FailureStrength.Definitive
                || (current.SuspectSince is { } since && at - since >= options.SuspectConfirmationWindow);

            if (confirmed)
            {
                return new ConnectionHealthEvaluation(
                    current,
                    MoveTo(counted with { SuspectSince = null }, ConnectionHealthState.ActionRequired, at),
                    ConnectionHealthOutcome.NotifyActionRequired);
            }

            counted = counted with { SuspectSince = current.SuspectSince ?? at };
        }

        return Progress(current, counted, at, options);
    }

    private static ConnectionHealthEvaluation Progress(
        ConnectionHealth current, ConnectionHealth counted, DateTimeOffset at, ConnectionHealthOptions options)
    {
        if (current.State == ConnectionHealthState.Dormant)
            return new ConnectionHealthEvaluation(current, counted, ConnectionHealthOutcome.None);

        if (at - counted.FirstFailureAt >= options.DormantAfter)
        {
            return new ConnectionHealthEvaluation(
                current, MoveTo(counted, ConnectionHealthState.Dormant, at), ConnectionHealthOutcome.GoDormant);
        }

        if (current.State == ConnectionHealthState.Failing)
            return new ConnectionHealthEvaluation(current, counted, ConnectionHealthOutcome.None);

        // "At least 2 h without success": from the last success, or from the first failure when there was none.
        var withoutSuccessSince = counted.LastSuccessAt ?? counted.FirstFailureAt;
        if (counted.ConsecutiveFailures >= options.FailingMinConsecutiveFailures
            && at - withoutSuccessSince >= options.FailingMinTimeWithoutSuccess)
        {
            return new ConnectionHealthEvaluation(
                current, MoveTo(counted, ConnectionHealthState.Failing, at), ConnectionHealthOutcome.NotifyFailing);
        }

        return new ConnectionHealthEvaluation(
            current, MoveTo(counted, ConnectionHealthState.Degraded, at), ConnectionHealthOutcome.None);
    }

    private static ConnectionHealth MoveTo(ConnectionHealth health, ConnectionHealthState state, DateTimeOffset at) =>
        health.State == state ? health : health with { State = state, StateChangedAt = at };

    private static string? Truncate(string? code) =>
        code is { Length: > ConnectionHealth.FailureCodeMaxLength } ? code[..ConnectionHealth.FailureCodeMaxLength] : code;
}
