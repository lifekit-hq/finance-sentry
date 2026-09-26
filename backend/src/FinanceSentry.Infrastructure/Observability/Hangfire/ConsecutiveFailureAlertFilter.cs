namespace FinanceSentry.Infrastructure.Observability.Hangfire;

using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Core.Interfaces;
using global::Hangfire.States;
using global::Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Raises a single Telegram-bound alert when a scheduled job hits N consecutive terminal failures (US4 /
/// FR-009), closing the silent-outage gap (the 636-consecutive-failure incident). Reuses the
/// Alerts→Companion→Telegram path proven by <c>ConsentExpiring</c>.
///
/// Implemented as an <see cref="IApplyStateFilter"/> so it observes the *final* applied state: while a job
/// still has retries left the applied state is <c>Scheduled</c>, so a <c>FailedState</c> here is terminal.
/// Transient errors (throttling, network) don't count toward the streak; a later <c>Succeeded</c> resets
/// it so the next failure can re-alert. Alert dispatch is fire-and-forget — a dispatch failure never
/// breaks the job (US4-AS3).
/// </summary>
public sealed class ConsecutiveFailureAlertFilter(
    IServiceProvider serviceProvider,
    IJobFailureStreakStore store,
    int threshold) : IApplyStateFilter
{
    private const int MaxErrorLength = 200;

    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IJobFailureStreakStore _store = store;
    private readonly int _threshold = threshold < 1 ? 1 : threshold;

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

    /// <summary>Core streak logic (public for unit testing). Never throws.</summary>
    public void RecordOutcome(string jobName, bool succeeded, Exception? error)
    {
        if (succeeded)
        {
            var current = _store.Get(jobName);
            if (current.Alerted)
                TryResolveAlert(jobName);
            if (current.Count != 0 || current.Alerted)
                _store.Set(jobName, JobFailureStreak.Empty);
            return;
        }

        // Transient/self-healing failures don't count toward the streak.
        if (JobFailureTransientClassifier.IsTransient(error))
            return;

        var streak = _store.Get(jobName);
        var count = streak.Count + 1;
        var alerted = streak.Alerted;

        if (count >= _threshold && !alerted)
            alerted = TryRaiseAlert(jobName, count, error);

        _store.Set(jobName, new JobFailureStreak(count, alerted));
    }

    // Returns true when the alert was raised without error, so a failed dispatch retries on the next
    // failure instead of being silently swallowed for the whole streak.
    private bool TryRaiseAlert(string jobName, int count, Exception? error)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IBankingTotalsReader>();
            var generator = scope.ServiceProvider.GetRequiredService<IAlertGeneratorService>();

            var referenceId = JobReferenceId(jobName);
            var lastError = Summarize(error);

            var userIds = users.GetActiveUserIdsAsync().GetAwaiter().GetResult();
            foreach (var userId in userIds)
            {
                generator.GenerateJobFailureAlertAsync(userId, referenceId, jobName, count, lastError)
                    .GetAwaiter().GetResult();
            }

            return true;
        }
        catch
        {
            // Fire-and-forget: alerting must never break job processing.
            return false;
        }
    }

    // Fire-and-forget, same discipline as TryRaiseAlert: a resolve failure must never break job processing.
    private void TryResolveAlert(string jobName)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IBankingTotalsReader>();
            var generator = scope.ServiceProvider.GetRequiredService<IAlertGeneratorService>();

            var referenceId = JobReferenceId(jobName);
            var userIds = users.GetActiveUserIdsAsync().GetAwaiter().GetResult();
            foreach (var userId in userIds)
            {
                generator.ResolveJobFailureAlertAsync(userId, referenceId).GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Fire-and-forget: alerting must never break job processing.
        }
    }

    /// <summary>Deterministic per-job id so alert dedup/resolve stays stable across runs.</summary>
    internal static Guid JobReferenceId(string jobName)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"jobfailure:{jobName}"));
        return new Guid(bytes);
    }

    private static string? Summarize(Exception? error)
    {
        if (error is null)
            return null;
        var message = error.Message;
        return message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];
    }
}
