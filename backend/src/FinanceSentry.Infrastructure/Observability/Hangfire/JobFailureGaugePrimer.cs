namespace FinanceSentry.Infrastructure.Observability.Hangfire;

using global::Hangfire;
using global::Hangfire.Common;
using global::Hangfire.Storage;

/// <summary>
/// Seeds <c>finance_job_consecutive_failures{job}</c> from the durable streak store at startup, so a
/// restart doesn't leave the series absent until each job's next terminal outcome (an alert rule watching
/// the gauge would see NoData instead of the live streak). Enumerates the registered recurring jobs from
/// Hangfire storage and keys each by <c>JobMetricsFilter.JobName</c>, the same key the streak store and the
/// gauge label use.
/// </summary>
public static class JobFailureGaugePrimer
{
    /// <summary>Reads the recurring jobs from <paramref name="storage"/> and seeds the gauge for each.</summary>
    public static void Seed(JobStorage storage, IJobFailureStreakStore store, JobMetrics metrics)
    {
        using var connection = storage.GetConnection();
        Seed(connection.GetRecurringJobs().Select(r => r.Job), store, metrics);
    }

    /// <summary>Publishes each job's persisted streak (0 when none).</summary>
    public static void Seed(IEnumerable<Job?> recurringJobs, IJobFailureStreakStore store, JobMetrics metrics)
    {
        foreach (var job in recurringJobs)
        {
            if (job is null)
                continue;

            var name = JobMetricsFilter.JobName(job);
            metrics.SetConsecutiveFailures(name, store.Get(name).Count);
        }
    }
}
