namespace FinanceSentry.Infrastructure.Observability;

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using global::Hangfire;

/// <summary>
/// Custom metrics for the scheduled-job fleet (FR-002). Registered as a singleton and shared with the
/// Hangfire <c>JobMetricsFilter</c>; the OpenTelemetry meter provider subscribes to <see cref="MeterName"/>
/// and the Prometheus exporter renders these at <c>/metrics</c>.
///
/// Labels are deliberately bounded to the job name only — no per-user/account/PII dimensions (FR-007).
/// </summary>
public sealed class JobMetrics : IDisposable
{
    /// <summary>OpenTelemetry meter name; must be registered via <c>AddMeter(JobMetrics.MeterName)</c>.</summary>
    public const string MeterName = "FinanceSentry.Jobs";

    private const string DefaultQueue = "default";

    private readonly Meter _meter;
    private readonly Counter<long> _succeeded;
    private readonly Counter<long> _failed;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _retentionRowsRemoved;

    // Data-retention/backup observability (feature 024). Timestamps are seeded from the DB at startup
    // by RetentionMetricsPrimer and updated live by the jobs; age gauges compute against now.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRetentionRun = new();
    private DateTimeOffset? _lastBackupVerified;

    // Current consecutive terminal-failure streak per job, written by ConsecutiveFailureAlertFilter.
    private readonly ConcurrentDictionary<string, int> _consecutiveFailures = new();

    public JobMetrics()
    {
        _meter = new Meter(MeterName);

        // No unit on the counters so the Prometheus exporter renders clean *_total names.
        _succeeded = _meter.CreateCounter<long>(
            "finance_jobs_succeeded",
            description: "Scheduled-job runs that reached the Succeeded state.");
        _failed = _meter.CreateCounter<long>(
            "finance_jobs_failed",
            description: "Scheduled-job runs that reached the terminal Failed state.");
        _duration = _meter.CreateHistogram<double>(
            "finance_job_duration",
            unit: "s",
            description: "Wall-clock duration of a scheduled-job run to its terminal state.");

        _meter.CreateObservableGauge(
            "finance_jobs_scheduled",
            ObserveScheduled,
            description: "Jobs currently enqueued or scheduled and awaiting execution.");

        _meter.CreateObservableGauge(
            "finance_job_consecutive_failures",
            ObserveConsecutiveFailures,
            description: "Consecutive terminal failures of a scheduled job since its last success (0 once it succeeds).");

        // ── Data retention & backups (feature 024) ──────────────────────────────────────────────
        _retentionRowsRemoved = _meter.CreateCounter<long>(
            "finance_retention_rows_removed",
            description: "Rows removed by the generic retention purge, by table.");

        _meter.CreateObservableGauge(
            "finance_retention_last_run_age_seconds",
            ObserveRetentionAge,
            unit: "s",
            description: "Seconds since the last successful retention run, by run type (SC-005).");

        _meter.CreateObservableGauge(
            "finance_backup_last_verified_age_seconds",
            ObserveBackupVerifiedAge,
            unit: "s",
            description: "Seconds since the last backup that provably restored (SC-002).");

        _meter.CreateObservableGauge(
            "finance_backup_last_verified_timestamp",
            ObserveBackupVerifiedTimestamp,
            unit: "s",
            description: "Unix time of the last provably-restorable backup (SC-002).");
    }

    /// <summary>Records a job that reached <c>Succeeded</c>.</summary>
    public void RecordSuccess(string job, double durationSeconds)
    {
        var tag = new KeyValuePair<string, object?>("job", job);
        _succeeded.Add(1, tag);
        if (durationSeconds >= 0)
            _duration.Record(durationSeconds, tag);
    }

    /// <summary>Records a job that reached the terminal <c>Failed</c> state.</summary>
    public void RecordFailure(string job, double durationSeconds)
    {
        var tag = new KeyValuePair<string, object?>("job", job);
        _failed.Add(1, tag);
        if (durationSeconds >= 0)
            _duration.Record(durationSeconds, tag);
    }

    /// <summary>
    /// Publishes a job's current consecutive-failure streak (0 after a success). The series stays at 0
    /// rather than disappearing so an alert rule sees the recovery.
    /// </summary>
    public void SetConsecutiveFailures(string job, int count)
        => _consecutiveFailures[job] = count;

    /// <summary>Records rows removed by a retention purge for a table (feature 024).</summary>
    public void RecordRetentionRowsRemoved(string table, long rows)
        => _retentionRowsRemoved.Add(rows, new KeyValuePair<string, object?>("table", table));

    /// <summary>Marks the most recent successful retention run of a given type (feature 024).</summary>
    public void SetLastRetentionRun(string runType, DateTimeOffset completedAt)
        => _lastRetentionRun[runType] = completedAt;

    /// <summary>Marks the most recent backup proven restorable by a drill (feature 024, SC-002).</summary>
    public void SetLastBackupVerified(DateTimeOffset verifiedAt)
        => _lastBackupVerified = verifiedAt;

    private static IEnumerable<Measurement<long>> ObserveScheduled()
    {
        yield return new Measurement<long>(SafeScheduledCount());
    }

    private IEnumerable<Measurement<int>> ObserveConsecutiveFailures()
    {
        foreach (var (job, count) in _consecutiveFailures)
            yield return new Measurement<int>(count, new KeyValuePair<string, object?>("job", job));
    }

    private IEnumerable<Measurement<double>> ObserveRetentionAge()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (runType, at) in _lastRetentionRun)
            yield return new Measurement<double>(
                (now - at).TotalSeconds, new KeyValuePair<string, object?>("run_type", runType));
    }

    private IEnumerable<Measurement<double>> ObserveBackupVerifiedAge()
    {
        if (_lastBackupVerified is { } at)
            yield return new Measurement<double>((DateTimeOffset.UtcNow - at).TotalSeconds);
    }

    private IEnumerable<Measurement<double>> ObserveBackupVerifiedTimestamp()
    {
        if (_lastBackupVerified is { } at)
            yield return new Measurement<double>(at.ToUnixTimeSeconds());
    }

    // Reads Hangfire's own counts defensively — storage may not be configured yet during startup/tests.
    private static long SafeScheduledCount()
    {
        try
        {
            var monitor = JobStorage.Current?.GetMonitoringApi();
            if (monitor is null)
                return 0;
            return monitor.EnqueuedCount(DefaultQueue) + monitor.ScheduledCount();
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose() => _meter.Dispose();
}
