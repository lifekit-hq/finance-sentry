namespace FinanceSentry.API.Hangfire;

using FinanceSentry.API.Migrations;
using FinanceSentry.Infrastructure.Fx;
using global::Hangfire;
using global::Hangfire.PostgreSql;
using Polly;
using Polly.Retry;

/// <summary>
/// Registers every recurring job once the host is listening, off the startup critical path.
/// Registering a job takes a storage lock (<c>hangfire:lock:recurring-job:&lt;id&gt;</c>, 15s wait). A
/// process killed while holding it leaves the row until <c>DistributedLockTimeout</c> (10 min by
/// default) passes, so a registration that throws on it cannot succeed by retrying for seconds — and
/// doing it before <c>app.Run()</c> turned that window into a crash loop (2026-09-30). Here the API
/// serves immediately and registration retries (a Polly <see cref="ResiliencePipeline"/> with capped exponential
/// backoff and an elapsed-time budget) for longer than the lock can live.
/// Registration is idempotent (<c>AddOrUpdate</c> only), so the whole pass is safe to repeat; the one-shot
/// startup enqueues live in <see cref="StartupSweeps"/> and never ride this retry loop.
/// When the budget is spent: Error log and <see cref="RecurringJobRegistrationState.Failed"/>, which the
/// <c>job-registration</c> readiness check reports. Any other exception is not handled here: it leaves
/// <c>ExecuteAsync</c>, and the host stops as for any unhandled background-service failure — the same
/// fail-fast outcome an exception had when registration ran inline in Program.cs.
/// </summary>
public sealed class RecurringJobRegistrationService(
    IServiceProvider services,
    IHostApplicationLifetime lifetime,
    RecurringJobRegistrationStatus status,
    StartupMigrationStatus migrationStatus,
    RecurringJobRegistrationOptions options,
    ILogger<RecurringJobRegistrationService> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private const string ExchangeRateJobId = "exchange-rate-refresh";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForHostStartedAsync(stoppingToken);
        await RunAsync(stoppingToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Registering writes to Hangfire's storage, which is the same database MigrateAllModules just
        // found unreachable. The "migrations" readiness check names that cause, and the restart it
        // prescribes registers this build's jobs. Jobs registered by earlier starts persist in storage.
        if (migrationStatus.MigrationsSkipped)
        {
            logger.LogError(
                "Skipping startup job registration: the database was unreachable when migrations ran, and " +
                "registering jobs writes to Hangfire's storage in that database. Restart the API once the " +
                "database is reachable so the skipped migrations run and this build's jobs are registered.");
            status.Set(RecurringJobRegistrationState.Skipped);
            return;
        }

        var clock = timeProvider ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        var attempts = 0;

        // Retry a lock timeout only while the wait that follows still fits in the budget; past it the
        // exception leaves the pipeline and is reported below.
        var pipeline = new ResiliencePipelineBuilder { TimeProvider = clock }
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is PostgreSqlDistributedLockException
                    && clock.GetElapsedTime(started) + DelayBeforeRetry(args.AttemptNumber) < options.Budget),
                DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(DelayBeforeRetry(args.AttemptNumber)),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "Recurring job registration hit a Hangfire lock timeout (attempt {Attempt}); retrying in {Delay}.",
                        args.AttemptNumber + 1, args.RetryDelay);
                    return default;
                },
            })
            .Build();

        try
        {
            await pipeline.ExecuteAsync(
                _ =>
                {
                    attempts++;
                    Register();
                    return default;
                },
                cancellationToken);
            status.Set(RecurringJobRegistrationState.Registered);
            logger.LogInformation("Recurring jobs registered (attempt {Attempt}).", attempts);
        }
        catch (PostgreSqlDistributedLockException ex)
        {
            logger.LogError(
                ex,
                "Recurring job registration gave up after {Attempts} attempts over {Elapsed} on a Hangfire lock timeout " +
                "(budget {Budget}). The API stays up with the recurring jobs already in storage; restart it to register this build's jobs.",
                attempts, clock.GetElapsedTime(started), options.Budget);
            status.Set(RecurringJobRegistrationState.Failed);
        }
    }

    // InitialDelay doubled per retry, capped at MaxDelay.
    private TimeSpan DelayBeforeRetry(int retryIndex)
    {
        var delay = options.InitialDelay;
        for (var i = 0; i < retryIndex && delay < options.MaxDelay; i++)
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaxDelay.Ticks));
        return delay < options.MaxDelay ? delay : options.MaxDelay;
    }

    private void Register()
    {
        JobRegistrationExtensions.RegisterAllModuleJobs(services);

        // Live FX rates: refresh daily.
        services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<ExchangeRateRefreshJob>(
            ExchangeRateJobId,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily());
    }

    private async Task WaitForHostStartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource();
        await using var startedRegistration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await using var stoppingRegistration = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
        await started.Task;
    }
}
