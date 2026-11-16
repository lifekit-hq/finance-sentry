namespace FinanceSentry.Infrastructure.Observability.Hangfire;

using global::Hangfire.Common;
using global::Hangfire.States;
using global::Hangfire.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hangfire state filter (FR-002): records per-job outcome and duration into <see cref="JobMetrics"/>
/// as each job reaches a terminal state. Registered globally via the Hangfire configuration.
///
/// <c>FailedState</c> is only reached after all automatic retries are exhausted, so it represents a
/// terminal failure — the same semantics the consecutive-failure gauge relies on. A terminal failure is also logged
/// with the job name and its exception, so an alert on the failure metric can be traced in the log.
/// </summary>
public sealed class JobMetricsFilter(JobMetrics metrics, ILogger<JobMetricsFilter>? logger = null) : IApplyStateFilter
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
            case FailedState failed:
                LogTerminalFailure(job, context.BackgroundJob?.Id, failed.Exception);
                // Duration is intentionally omitted for failures (no reliable processing-start here);
                // the histogram stays meaningful from succeeded runs, the counter captures the failure.
                _metrics.RecordFailure(job, -1);
                break;
        }
    }

    /// <summary>Names the job and its exception in the log when it fails for good, so the failure metric can be traced.</summary>
    private void LogTerminalFailure(string job, string? jobId, Exception? exception)
        => logger?.LogError(exception, "Background job {Job} failed after its retries (job id {JobId})", job, jobId);

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
