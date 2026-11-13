namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using Hangfire;

/// <summary>
/// Weekly refresh of the reference universe (S&amp;P 500, Nasdaq-100, FinanceDatabase sector and industry lists,
/// theme lists). Sunday morning UTC: FinanceDatabase's own automated update runs on Sundays. A failed dataset
/// makes the job end Failed (see <see cref="ReferenceUniverseRefresher"/>), never green-and-stale.
/// </summary>
public sealed class ReferenceUniverseRefreshJob(ReferenceUniverseRefresher refresher)
{
    public const string RecurringJobId = "reference-universe-refresh";

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default)
        => await refresher.RefreshAsync(ct);
}
