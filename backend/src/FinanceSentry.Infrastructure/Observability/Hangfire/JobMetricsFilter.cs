namespace FinanceSentry.Infrastructure.Observability.Hangfire;

using global::Hangfire.Common;
using global::Hangfire.States;
using global::Hangfire.Storage;

/// <summary>
/// Hangfire state filter (FR-002): records per-job outcome and duration into <see cref="JobMetrics"/>
/// as each job reaches a terminal state. Registered globally via the Hangfire configuration.
///
/// <c>FailedState</c> is only reached after all automatic retries are exhausted, so it represents a
/// terminal failure — the same semantics the consecutive-failure gauge relies on.
/// </summary>
public sealed class JobMetricsFilter(JobMetrics metrics) : IApplyStateFilter
{
    private const double MillisecondsPerSecond = 1000.0;

    private readonly JobMetrics _metrics = metrics;

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        var job = JobName(context.BackgroundJob?.Job);

        switch (context.NewState)
        {
            case SucceededState succeeded:
                _metrics.RecordSuccess(job, succeeded.PerformanceDuration / MillisecondsPerSecond);
                break;
            case FailedState:
                // Duration is intentionally omitted for failures (no reliable processing-start here);
                // the histogram stays meaningful from succeeded runs, the counter captures the failure.
                _metrics.RecordFailure(job, -1);
                break;
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        // No-op: we only record on entry to a terminal state.
    }

    /// <summary>Stable, bounded-cardinality job label — the declaring type + method (no arguments).</summary>
    internal static string JobName(Job? job)
    {
        if (job?.Type is null || job.Method is null)
            return "unknown";
        return $"{job.Type.Name}.{job.Method.Name}";
    }
}
