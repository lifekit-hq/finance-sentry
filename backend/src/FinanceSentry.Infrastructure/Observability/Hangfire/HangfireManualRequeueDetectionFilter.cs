namespace FinanceSentry.Infrastructure.Observability.Hangfire;

using global::Hangfire.States;

/// <summary>
/// Marks an operator-initiated Hangfire dashboard Requeue for <see cref="HangfireTracingFilter"/>
/// (#616 follow-up): a state-election signal the requeue itself produces, not an inference from job
/// age. It fires when a job is elected into <see cref="EnqueuedState"/> straight from a state only a
/// manual action reaches from there — <see cref="FailedState"/>, <see cref="SucceededState"/>,
/// <see cref="DeletedState"/> — or from a <see cref="ScheduledState"/> that already carries a positive
/// <c>RetryCount</c>, i.e. a retry-scheduled state an operator jumped ahead of its backoff.
///
/// <para>
/// <c>AutomaticRetryAttribute</c> never elects <see cref="EnqueuedState"/> itself: on a failure under
/// its attempt budget it elects <see cref="ScheduledState"/> for the next attempt, and once the budget
/// is exhausted it leaves the job in <see cref="FailedState"/>. So any election into
/// <see cref="EnqueuedState"/> from one of the states above is never its doing — this filter does not
/// need to identify "who" produced the election beyond ruling that out structurally.
/// </para>
///
/// <para>
/// A plain <see cref="ScheduledState"/> with no <c>RetryCount</c> is deliberately excluded: that is an
/// ad-hoc delayed job reaching its scheduled time for the first time, still a first attempt, and must
/// keep today's parent-child behaviour.
/// </para>
/// </summary>
public sealed class HangfireManualRequeueDetectionFilter : IElectStateFilter
{
    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not EnqueuedState)
            return;

        if (IsManualRequeueTransition(context))
            context.SetJobParameter(HangfireTracingFilter.ManualRequeueParameter, true);
    }

    private static bool IsManualRequeueTransition(ElectStateContext context)
    {
        if (context.CurrentState == FailedState.StateName
            || context.CurrentState == SucceededState.StateName
            || context.CurrentState == DeletedState.StateName)
            return true;

        return context.CurrentState == ScheduledState.StateName
            && context.GetJobParameter<int>(HangfireTracingFilter.RetryCountParameter, true) > 0;
    }
}
