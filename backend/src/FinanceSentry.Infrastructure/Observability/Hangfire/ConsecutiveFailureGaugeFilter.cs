namespace FinanceSentry.Infrastructure.Observability.Hangfire;

using global::Hangfire.States;
using global::Hangfire.Storage;

/// <summary>
/// Feeds <c>finance_job_consecutive_failures{job}</c>, the gauge the Grafana rule
/// <c>finance-job-consecutive-failures</c> alerts on (#935). Implemented as an <see cref="IApplyStateFilter"/>
/// so it observes the *final* applied state: while a job still has retries left the applied state is
/// <c>Scheduled</c>, so a <c>FailedState</c> here is terminal. A terminal failure increments the streak, a
/// <c>Succeeded</c> resets it to 0, and transient errors (throttling, network) are ignored. The streak is kept
/// in <see cref="IJobFailureStreakStore"/> so a restart doesn't reset a live streak.
/// </summary>
public sealed class ConsecutiveFailureGaugeFilter(IJobFailureStreakStore store, JobMetrics metrics) : IApplyStateFilter
{
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        var job = JobMetricsFilter.JobName(context.BackgroundJob?.Job);

        switch (context.NewState)
        {
            case SucceededState:
                RecordOutcome(job, succeeded: true, error: null);
                break;
            case FailedState failed:
                RecordOutcome(job, succeeded: false, error: failed.Exception);
                break;
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        // No-op.
    }

    /// <summary>Core streak logic (public for unit testing).</summary>
    public void RecordOutcome(string jobName, bool succeeded, Exception? error)
    {
        if (succeeded)
        {
            if (store.Get(jobName) != JobFailureStreak.Empty)
                store.Set(jobName, JobFailureStreak.Empty);
            metrics.SetConsecutiveFailures(jobName, 0);
            return;
        }

        // Transient/self-healing failures don't count toward the streak.
        if (JobFailureTransientClassifier.IsTransient(error))
            return;

        var count = store.Get(jobName).Count + 1;
        store.Set(jobName, new JobFailureStreak(count, Alerted: false));
        metrics.SetConsecutiveFailures(jobName, count);
    }
}
