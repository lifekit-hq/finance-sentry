namespace FinanceSentry.API.Hangfire;

using FinanceSentry.API.Migrations;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using global::Hangfire;

/// <summary>
/// Enqueues the one-shot startup jobs — each module's <see cref="IStartupSweep"/> (BankSync: reap
/// orphaned syncs, then reschedule active accounts), then the immediate FX refresh. Runs exactly once
/// per process, independent of recurring-job registration and its lock retries: these enqueues are not
/// idempotent. Skipped when migrations were skipped, because enqueueing writes to the same unreachable
/// database (the recurring-job registration service logs that).
/// </summary>
public static class StartupSweeps
{
    public static void Enqueue(IServiceProvider services)
    {
        if (services.GetRequiredService<StartupMigrationStatus>().MigrationsSkipped)
            return;

        foreach (var sweep in services.GetServices<IStartupSweep>())
            sweep.Enqueue(services);

        // Refresh live FX rates once immediately so we leave the hardcoded fallback table behind as
        // soon as the app is up (the daily recurring registration lives in RecurringJobRegistrationService).
        services.GetRequiredService<IBackgroundJobClient>()
            .Enqueue<ExchangeRateRefreshJob>(job => job.RunAsync(CancellationToken.None));
    }
}
