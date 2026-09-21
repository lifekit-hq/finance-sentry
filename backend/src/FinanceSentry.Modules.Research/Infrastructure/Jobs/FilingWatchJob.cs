namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hourly Hangfire job (T2, ledger-heartbeat design): raises a FilingLanded alert when a 10-K, 10-Q
/// or 8-K lands on a holding or a thesis-proxy ticker. EDGAR's submissions feed has no push
/// mechanism, so this rereads it every hour and relies on <see cref="IAlertGeneratorService"/>'s
/// dedup — per (ticker, accession number), which EDGAR never reuses — to make a re-read a no-op for
/// filings already seen. Only filings dated today are considered, so a stale or backfilled
/// submissions response can never surface a filing from before this job existed. EDGAR is an
/// external dependency with no contract — a missing field, an empty submissions list or a failed
/// fetch already come back as an empty result from <see cref="ISecEdgarService"/>, so a provider
/// hiccup here means no alert this run, never a job failure. Rare by design (~0.2 fires/day in
/// earnings weeks).
/// </summary>
public sealed class FilingWatchJob(
    IBankingTotalsReader banking,
    IBrokerageHoldingsReader brokerage,
    IThesisRepository theses,
    ISecEdgarService secEdgar,
    IAlertGeneratorService alerts,
    ILogger<FilingWatchJob> logger)
{
    private const string EquityInstrumentType = "STK";
    private const int FilingsLookbackLimit = 10;

    private static readonly string[] CoveredForms = ["10-K", "10-Q", "8-K"];

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
        var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var holding in await brokerage.GetHoldingsAsync(userId, ct))
        {
            if (string.Equals(holding.InstrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase))
            {
                tickers.Add(holding.Symbol);
            }
        }

        foreach (var thesis in await theses.ListAsync(userId, ct))
        {
            tickers.Add(thesis.Ticker);
            foreach (var trigger in thesis.InvalidationTriggers)
            {
                if (!string.IsNullOrWhiteSpace(trigger.ProxyTicker))
                {
                    tickers.Add(trigger.ProxyTicker);
                }
            }
        }

        foreach (var ticker in tickers)
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

        foreach (var filing in filings)
        {
            if (filing.FilingDate != today || string.IsNullOrEmpty(filing.AccessionNumber))
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
