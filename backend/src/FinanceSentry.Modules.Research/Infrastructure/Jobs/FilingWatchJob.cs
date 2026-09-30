namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hourly Hangfire job (T2, ledger-heartbeat design): raises a FilingLanded alert when a periodic or
/// current report lands on a holding, thesis or thesis-proxy ticker
/// (<see cref="LookaheadUniverse.FilingTickersAsync"/>) — 10-K, 10-Q and 8-K for domestic issuers, and
/// 6-K, 20-F and 40-F for foreign private issuers, which never file the domestic forms (#698: without
/// them a foreign-listed holding could never alert). EDGAR's submissions feed has no push mechanism,
/// so this rereads it every hour and relies on <see cref="IAlertGeneratorService"/>'s dedup — once per
/// (ticker, accession number), which EDGAR never reuses — to make a re-read a no-op for filings already
/// seen. A filing is considered for <see cref="FilingDateLookbackDays"/> days after its filing date
/// rather than only on the day itself: every deploy restarts the scheduler, and a same-day-only window
/// silently lost any filing whose day happened to miss the remaining hourly runs. The window is still
/// short enough that a stale or backfilled submissions response cannot surface an old filing. EDGAR is
/// an external dependency with no contract — a missing field, an empty submissions list or a failed
/// fetch already come back as an empty result from <see cref="ISecEdgarService"/> (which keeps the
/// submissions cache shorter than the hourly cadence and never caches a failed fetch), so a provider
/// hiccup here means no alert this run, never a job failure. Rare by design (~0.2 fires/day in
/// earnings weeks).
/// </summary>
public sealed class FilingWatchJob(
    IBankingTotalsReader banking,
    LookaheadUniverse universe,
    ISecEdgarService secEdgar,
    IAlertGeneratorService alerts,
    ILogger<FilingWatchJob> logger)
{
    /// <summary>The Hangfire recurring-job id this detector is scheduled under.</summary>
    public const string RecurringJobId = "filing-watch";

    private const int FilingsLookbackLimit = 10;

    /// <summary>
    /// How many days back from today a filing date still counts as newly landed. Three days covers a
    /// Friday filing on the following Monday (the job skips weekends) and a filing whose day was lost
    /// to a restart.
    /// </summary>
    private const int FilingDateLookbackDays = 3;

    private static readonly string[] CoveredForms = ["10-K", "10-Q", "8-K", "6-K", "20-F", "40-F"];

    public Task ExecuteAsync(CancellationToken ct = default) => ExecuteAsync(DateTime.UtcNow, ct);

    /// <summary>Overload taking the reference instant explicitly, so tests aren't at the mercy of the day they run on.</summary>
    public async Task ExecuteAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        if (!IsUsTradingDay(nowUtc))
        {
            logger.LogDebug("FilingWatch: not a US trading day, skipping.");
            return;
        }

        var userIds = await banking.GetActiveUserIdsAsync(ct);
        if (userIds.Count == 0)
        {
            logger.LogDebug("FilingWatch: no active users found, skipping.");
            return;
        }

        var today = DateOnly.FromDateTime(nowUtc);

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, today, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FilingWatch: error processing user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, DateOnly today, CancellationToken ct)
    {
        foreach (var ticker in await universe.FilingTickersAsync(userId, ct))
        {
            await ProcessTickerAsync(userId, ticker, today, ct);
        }
    }

    private async Task ProcessTickerAsync(Guid userId, string ticker, DateOnly today, CancellationToken ct)
    {
        IReadOnlyList<EdgarFiling> filings;
        try
        {
            filings = await secEdgar.GetRecentFilingsAsync(ticker, CoveredForms, FilingsLookbackLimit, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // EDGAR submissions have no contract — a failed fetch means no signal this run, not a
            // job failure. The hourly cron simply tries again next hour.
            logger.LogWarning(ex, "FilingWatch: EDGAR submissions fetch failed for {Ticker}", ticker);
            return;
        }

        var earliest = today.AddDays(-FilingDateLookbackDays);

        foreach (var filing in filings)
        {
            if (filing.FilingDate < earliest || filing.FilingDate > today
                || string.IsNullOrEmpty(filing.AccessionNumber))
            {
                continue;
            }

            await alerts.GenerateFilingLandedAlertAsync(
                userId, ticker, filing.Form, filing.FilingDate, filing.AccessionNumber, filing.DocumentUrl, ct);
        }
    }

    // Exchange-holiday-blind by design, matching FreshnessEvaluator's weekday approximation elsewhere
    // in Research — a holiday run simply finds nothing new to alert on.
    private static bool IsUsTradingDay(DateTime utcNow)
        => utcNow.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
}
