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
/// <item>A rate-limited answer counts like a transient one, is never a credential signal, and may carry the time
/// before which no scheduled run calls the provider (<see cref="ConnectionHealth.NotBefore"/>).</item>
/// <item>A not-ready answer is not counted while the last success is younger than the source's
/// <see cref="ConnectionHealthSourceOptions.NotReadyDeadline"/> (a connection that never succeeded is aged from when it was
/// connected); past it, it counts like a transient failure.</item>
/// <item>An internal failure is ours, not the provider's: it leaves the health as it was.</item>
/// <item>A scheduled run skips an ActionRequired connection (<see cref="ScheduledRunPausedUntil"/>), except for one
/// probe per <see cref="ConnectionHealthOptions.ActionRequiredProbeInterval"/>; a success clears the state as usual.
/// An active provider's Dormant connection is slowed the same way, to one run per
/// <see cref="ConnectionHealthOptions.DormantSyncInterval"/> (<see cref="DormantPausedUntil"/>).</item>
/// <item>A suspect credential failure is looked at again twice and no more (<see cref="SuspectRecheckAfter"/>): soon, so a
/// blip clears in minutes, and once the confirmation window has passed, so a real loss is confirmed on time.</item>
/// </list>
/// </summary>
public static class ConnectionHealthPolicy
{
    /// <summary>
    /// How early a probe may start. A daily cron run starts about when the previous one did, a few seconds before
    /// that run's failure was stamped; without the slack a once-a-day source would skip its probe every other day.
    /// </summary>
    private static readonly TimeSpan ProbeSlack = TimeSpan.FromMinutes(5);

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
            NotBefore = null,
        };

        var userWasTold = current.State is ConnectionHealthState.Failing
            or ConnectionHealthState.ActionRequired
            or ConnectionHealthState.Dormant;

        return new ConnectionHealthEvaluation(current, next, userWasTold ? ConnectionHealthOutcome.Resolve : ConnectionHealthOutcome.None);
    }

    /// <param name="source">
    /// The failing source's own values; null reads as the defaults (a not-ready answer has no grace).
    /// </param>
    /// <param name="connectedAt">
    /// When the connection was made; ages a not-ready answer on a connection that has not succeeded yet.
    /// </param>
    public static ConnectionHealthEvaluation RecordFailure(
        ConnectionHealth current,
        ProviderFailure failure,
        DateTimeOffset at,
        ConnectionHealthOptions options,
        ConnectionHealthSourceOptions? source = null,
        DateTimeOffset? connectedAt = null)
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
            NotBefore = LaterOf(current.NotBefore is { } held && held > at ? held : null, failure.NotBefore),
        };

        if (current.State == ConnectionHealthState.ActionRequired)
            return new ConnectionHealthEvaluation(current, counted, ConnectionHealthOutcome.None);

        if (failure.Class == FailureClass.NotReady && IsWithinNotReadyGrace(current, at, source, connectedAt))
            return new ConnectionHealthEvaluation(current, current, ConnectionHealthOutcome.None);

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

    /// <summary>
    /// When a scheduled run may next call the provider, or null when it may go ahead now. Two things pause it, and
    /// the later wins:
    /// <list type="bullet">
    /// <item>The provider named a time (<see cref="ConnectionHealth.NotBefore"/>, a rate limit's <c>Retry-After</c>)
    /// that has not come.</item>
    /// <item>The connection is ActionRequired and its probe is not due. The probe is timed from
    /// <see cref="ConnectionHealth.LastFailureAt"/>, which a failed probe renews, so no field is added; a
    /// connection with no failure on record has nothing to time from and is probed at once. A probe may start
    /// <see cref="ProbeSlack"/> early.</item>
    /// </list>
    /// With <paramref name="slowDormant"/> (the provider is in <see cref="ConnectionHealthOptions.ActiveProviders"/>), a
    /// Dormant connection is paused too (<see cref="DormantPausedUntil"/>). A run the person triggers does not ask: it
    /// always goes ahead.
    /// </summary>
    public static DateTimeOffset? ScheduledRunPausedUntil(
        ConnectionHealth health, DateTimeOffset now, ConnectionHealthOptions options, bool slowDormant = false)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(options);

        var notBefore = NotBeforePausedUntil(health, now);
        var probeAt = health.State == ConnectionHealthState.ActionRequired
            ? NextRunAt(health, now, options.ActionRequiredProbeInterval)
            : null;
        var dormant = slowDormant ? DormantPausedUntil(health, now, options) : null;

        return LaterOf(LaterOf(notBefore, probeAt), dormant);
    }

    /// <summary>
    /// When a Dormant connection's slowed cadence next lets a scheduled run try it, or null when it may go ahead now
    /// (or is not Dormant): one run per <see cref="ConnectionHealthOptions.DormantSyncInterval"/>, timed from
    /// <see cref="ConnectionHealth.LastFailureAt"/> as the ActionRequired probe is, so a failed run renews the pause and a
    /// reconnect that clears the last failure lets the next run through. A success leaves Dormant, so the cadence is
    /// back at once. Whether it applies is the caller's: only to a provider in
    /// <see cref="ConnectionHealthOptions.ActiveProviders"/>.
    /// </summary>
    public static DateTimeOffset? DormantPausedUntil(ConnectionHealth health, DateTimeOffset now, ConnectionHealthOptions options)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(options);

        return health.State == ConnectionHealthState.Dormant
            ? NextRunAt(health, now, options.DormantSyncInterval)
            : null;
    }

    /// <summary>
    /// How long until the next look at a suspect credential failure, or null for none. <paramref name="recheck"/> is how
    /// many looks have run (0 after the scheduled run's own attempt); <paramref name="before"/> and
    /// <paramref name="after"/> are the health around the attempt just made.
    /// <list type="bullet">
    /// <item>The attempt that raised the suspicion earns a quick look after
    /// <see cref="ConnectionHealthOptions.SuspectQuickRetryDelay"/>: a success clears it at once.</item>
    /// <item>A quick look that leaves it standing earns the confirming look, once
    /// <see cref="ConnectionHealthOptions.SuspectConfirmationWindow"/> has passed since it was raised.</item>
    /// </list>
    /// Never more: a sign-in is not repeated beyond that, since failed ones are what lock accounts. Nothing once the
    /// failure is confirmed (ActionRequired), cleared, or the connection is Dormant, and never sooner than
    /// <see cref="ConnectionHealth.NotBefore"/>. Whether a look is made is the caller's: only for a provider in
    /// <see cref="ConnectionHealthOptions.ActiveProviders"/>.
    /// </summary>
    public static TimeSpan? SuspectRecheckAfter(
        ConnectionHealth before, ConnectionHealth after, int recheck, DateTimeOffset now, ConnectionHealthOptions options)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(options);

        if (after.SuspectSince is not { } since
            || after.State is ConnectionHealthState.ActionRequired or ConnectionHealthState.Dormant
            || options.SuspectQuickRetryDelay <= TimeSpan.Zero)
            return null;

        TimeSpan delay;
        switch (recheck)
        {
            case 0 when before.SuspectSince is null:
                delay = options.SuspectQuickRetryDelay;
                break;
            case 1:
                delay = since + options.SuspectConfirmationWindow - now;
                if (delay <= TimeSpan.Zero)
                    return null;
                break;
            default:
                return null;
        }

        return after.NotBefore is { } notBefore && notBefore - now > delay ? notBefore - now : delay;
    }

    /// <summary>
    /// The time the provider asked scheduled runs to wait for (<see cref="ConnectionHealth.NotBefore"/>), or null
    /// when it has passed or none was named. For a scheduler that parks an unusable credential its own way and so
    /// asks only about this.
    /// </summary>
    public static DateTimeOffset? NotBeforePausedUntil(ConnectionHealth health, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(health);
        return health.NotBefore is { } named && now < named ? named : null;
    }

    /// <summary>
    /// A not-ready answer is the provider still preparing what we asked for. It is not counted while the last
    /// success (or, before the first one, the connection) is younger than the source's deadline; a source with no
    /// deadline has no grace, and a connection with neither a success nor a start time has no age to excuse it,
    /// so both count.
    /// </summary>
    private static bool IsWithinNotReadyGrace(
        ConnectionHealth current, DateTimeOffset at, ConnectionHealthSourceOptions? source, DateTimeOffset? connectedAt) =>
        source?.NotReadyDeadline is { } deadline
        && (current.LastSuccessAt ?? connectedAt) is { } since
        && at - since < deadline;

    // The next run of a paused connection, timed from its last failure; null when it is due (it may start ProbeSlack
    // early) or there is no failure on record to time from.
    private static DateTimeOffset? NextRunAt(ConnectionHealth health, DateTimeOffset now, TimeSpan interval)
    {
        if (health.LastFailureAt is not { } lastFailureAt)
            return null;

        var nextRunAt = lastFailureAt + interval;
        return now < nextRunAt - ProbeSlack ? nextRunAt : null;
    }

    private static DateTimeOffset? LaterOf(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a >= b ? a : b;

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
