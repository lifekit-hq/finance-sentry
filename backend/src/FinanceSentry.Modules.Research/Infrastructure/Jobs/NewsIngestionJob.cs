namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class NewsIngestionJob(
    ResearchDbContext research,
    IBrokerageHoldingsReader brokerage,
    ICryptoHoldingsReader crypto,
    IMarketNewsService news,
    INewsSourceRepository sourceRepo,
    INewsRepository newsRepo,
    NewsSourceFetcher fetcher,
    IAlertGeneratorService alerts,
    IBankingTotalsReader banking,
    LookaheadUniverse universe,
    IOptions<CoverageOptions> coverageOptions,
    TimeProvider clock,
    ILogger<NewsIngestionJob> logger)
{
    /// <summary>The budget of held, thesis and watchlist names per run; coverage names ride on top of it (<see cref="CoverageOptions.NewsCoverageTickersPerRun"/>).</summary>
    private const int MaxTickersPerRun = 60;

    // Holding rows whose symbol is not an exchange ticker: broker cash ledgers ("USD Cash") and Inzhur's
    // fund/bond names. Querying news for them only spends the per-run ticker budget.
    private static readonly HashSet<string> NonTickerInstrumentTypes = new(StringComparer.OrdinalIgnoreCase) { "CASH", "REIT", "BOND" };

    /// <summary>
    /// Reads news for the book first (watchlist, then thesis and holdings names, at most <c>MaxTickersPerRun</c>), then for a
    /// rotating slice of the coverage universe the book does not already name, so a coverage list of 100 neither starves a
    /// held name nor goes unread: every covered name is read once every few runs.
    /// </summary>
    public async Task IngestTickersAsync(CancellationToken ct = default)
    {
        var priority = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string ticker)
        {
            if (!string.IsNullOrWhiteSpace(ticker) && seen.Add(ticker))
            {
                priority.Add(ticker);
            }
        }

        var thesisTickers = await research.Theses.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Select(t => t.Ticker)
            .ToListAsync(ct);
        var watchlistTickers = await research.WatchlistItems.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Select(w => w.Ticker)
            .ToListAsync(ct);

        var userIds = await research.WatchlistItems.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Select(w => w.UserId)
            .Union(research.Theses.AsNoTracking().IgnoreQueryFilters([OwnerQueryFilter.Name]).Select(t => t.UserId))
            .Union(research.CoverageMembers.AsNoTracking().IgnoreQueryFilters([OwnerQueryFilter.Name]).Select(m => m.UserId))
            .Distinct()
            .ToListAsync(ct);

        foreach (var t in watchlistTickers)
        {
            Add(t);
        }

        foreach (var t in thesisTickers)
        {
            Add(t);
        }

        var coverageTickers = new List<string>();
        foreach (var userId in userIds)
        {
            foreach (var h in await brokerage.GetHoldingsAsync(userId, ct))
            {
                if (NonTickerInstrumentTypes.Contains(h.InstrumentType)) continue;
                Add(h.Symbol);
            }

            foreach (var h in await crypto.GetHoldingsAsync(userId, ct))
            {
                if (h.IsVenueFiat) continue;
                Add(h.Asset + "-USD");
            }

            coverageTickers.AddRange(await universe.CoverageTickersByPriorityAsync(userId, ct));
        }

        var toFetch = priority.Take(MaxTickersPerRun).ToList();

        // Coverage names the book's budget does not already read (a covered name cut from the book's tail still counts as coverage), held-first within each user and then in ticker order, read in
        // rotating slices so a long list is covered across runs without taking the book's budget.
        var options = coverageOptions.Value;
        var extras = coverageTickers
            .Where(t => !toFetch.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var slice = TickerRotation.Slice(
            extras, options.NewsCoverageTickersPerRun, TickerRotation.RunIndex(clock.GetUtcNow(), options.NewsRotationMinutes));
        toFetch.AddRange(slice);

        var inserted = await news.IngestForTickersAsync(toFetch.ToArray(), ct);
        logger.LogInformation(
            "NewsIngestionJob (tickers) ingested {Count} new articles across {Tickers} tickers ({Coverage} of {CoverageTotal} coverage names this run)",
            inserted, toFetch.Count, slice.Count, extras.Count);

        // Registered market-wide + thesis sources ride the same 30-min cadence (feature 030, R9).
        await IngestRegisteredSourcesAsync(ct);
    }

    public async Task IngestFedAsync(CancellationToken ct = default)
    {
        var inserted = await news.IngestFedPressAsync(ct);
        logger.LogInformation("NewsIngestionJob (fed) ingested {Count} new articles", inserted);
    }

    /// <summary>
    /// Iterates the enabled <c>news_sources</c> registry (feature 030, FR-007/FR-008). RSS feeds go
    /// through the existing parser; Page sources through their <see cref="INewsPageSource"/>. Articles
    /// are tagged with the source's thesis, and each source tracks its own consecutive failures — a
    /// sync-failure alert fires at the threshold while other sources keep going (FR-009).
    /// </summary>
    public async Task IngestRegisteredSourcesAsync(CancellationToken ct = default)
    {
        var sources = await sourceRepo.ListEnabledAsync(ct);
        foreach (var source in sources)
        {
            await IngestSourceAsync(source, ct);
        }
    }

    private async Task IngestSourceAsync(NewsSource source, CancellationToken ct)
    {
        try
        {
            var articles = await fetcher.FetchAsync(source, ct);
            var inserted = await newsRepo.InsertNewAsync(articles, ct);
            NewsSourceHealthTracker.RecordSuccess(source);
            await sourceRepo.UpdateAsync(source, ct);
            logger.LogInformation(
                "News source {Source}: {Fetched} fetched, {Inserted} new", source.Name, articles.Count, inserted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var outcome = NewsSourceHealthTracker.RecordFailure(source, ex.Message);
            await sourceRepo.UpdateAsync(source, ct);

            if (outcome == NewsSourceFailureOutcome.Disable)
            {
                logger.LogError(ex,
                    "News source {Source} auto-disabled after {Consecutive} consecutive failures",
                    source.Name, source.ConsecutiveFailures);
                await RaiseFailureAlertAsync(
                    source.Name,
                    $"Auto-disabled after {source.ConsecutiveFailures} consecutive failures: {ex.Message}",
                    ct);
            }
            else
            {
                // Ongoing failures below the disable threshold are logged at Warning to avoid flooding the
                // error stream; the one-time alert still fires when the alert threshold is first crossed.
                logger.LogWarning(
                    "News source {Source} failed ({Consecutive} consecutive)", source.Name, source.ConsecutiveFailures);

                if (outcome == NewsSourceFailureOutcome.Alert)
                {
                    await RaiseFailureAlertAsync(source.Name, ex.Message, ct);
                }
            }
        }
    }

    private async Task RaiseFailureAlertAsync(string sourceName, string reason, CancellationToken ct)
    {
        var provider = $"news-source:{sourceName}";
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        foreach (var userId in userIds)
        {
            await alerts.GenerateSyncFailureAlertAsync(userId, provider, null, null, reason, SyncFailureClass.Unknown, ct);
        }
    }
}
