namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class QuoteCacheRepository(ResearchDbContext db) : IQuoteCacheRepository
{
    public async Task<IReadOnlyDictionary<string, QuoteCacheEntry>> GetFreshAsync(
        IReadOnlyCollection<string> tickers, TimeSpan maxAge, CancellationToken ct = default)
    {
        if (tickers.Count == 0)
        {
            return new Dictionary<string, QuoteCacheEntry>();
        }

        var threshold = DateTimeOffset.UtcNow - maxAge;
        var normalized = tickers.Select(t => t.ToUpperInvariant()).ToArray();

        var rows = await db.QuoteCache.AsNoTracking()
            .Where(q => normalized.Contains(q.Ticker) && q.FetchedAt >= threshold)
            .ToListAsync(ct);

        return rows.ToDictionary(q => q.Ticker, q => q);
    }

    /// <summary>
    /// Refreshes the cached quotes with one <c>INSERT ... ON CONFLICT</c> over the <c>Ticker</c> primary key: the
    /// write is atomic, so two overlapping refreshes update the same rows instead of the loser failing on the key,
    /// and no read precedes it. A repeated ticker in the batch keeps its last entry.
    ///
    /// The statement runs at once and stages nothing in the shared <see cref="ResearchDbContext"/>, which is what
    /// issue #626 needs: this context is scoped, so it is the *caller's* unit of work, and every caller treats a
    /// quote refresh as best-effort and swallows the failure (<c>ThesisEventRecorder.TryGetPricesAsync</c>, the
    /// monitor, the snapshot jobs). Rows left staged by a failed write would survive the swallow and make the next
    /// unrelated <c>SaveChangesAsync</c> throw — how a quote-cache fault surfaced as <c>save_thesis</c> failing on
    /// every call while reads were fine. A cache is best-effort; its failure must not be contagious. A quote
    /// already tracked by this context is not refreshed.
    /// </summary>
    public async Task UpsertManyAsync(IReadOnlyCollection<QuoteCacheEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var incoming = entries
            .GroupBy(e => e.Ticker)
            .Select(g => g.Last())
            .ToList();

        var tickers = incoming.Select(e => e.Ticker).ToArray();
        var resolvedTickers = incoming.Select(e => e.ResolvedTicker).ToArray();
        var prices = incoming.Select(e => e.Price).ToArray();
        var previousCloses = incoming.Select(e => e.PreviousClose).ToArray();
        var currencies = incoming.Select(e => e.Currency).ToArray();
        var fetchedAts = incoming.Select(e => e.FetchedAt).ToArray();
        var marketStates = incoming.Select(e => e.MarketState).ToArray();
        var sessions = incoming.Select(e => e.Session).ToArray();
        var isStales = incoming.Select(e => e.IsStale).ToArray();
        var sourcePriceTimes = incoming.Select(e => e.SourcePriceTime).ToArray();
        var regularMarketTimes = incoming.Select(e => e.RegularMarketTime).ToArray();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO research.quote_cache
                ("Ticker", "ResolvedTicker", "Price", "PreviousClose", "Currency", "FetchedAt", "MarketState",
                 "Session", "IsStale", "SourcePriceTime", "RegularMarketTime")
            SELECT * FROM unnest(
                {tickers}::varchar[], {resolvedTickers}::varchar[], {prices}::numeric[], {previousCloses}::numeric[],
                {currencies}::varchar[], {fetchedAts}::timestamptz[], {marketStates}::varchar[], {sessions}::varchar[],
                {isStales}::boolean[], {sourcePriceTimes}::timestamptz[], {regularMarketTimes}::timestamptz[])
            ON CONFLICT ("Ticker") DO UPDATE SET
                "ResolvedTicker" = EXCLUDED."ResolvedTicker",
                "Price" = EXCLUDED."Price",
                "PreviousClose" = EXCLUDED."PreviousClose",
                "Currency" = EXCLUDED."Currency",
                "FetchedAt" = EXCLUDED."FetchedAt",
                "MarketState" = EXCLUDED."MarketState",
                "Session" = EXCLUDED."Session",
                "IsStale" = EXCLUDED."IsStale",
                "SourcePriceTime" = EXCLUDED."SourcePriceTime",
                "RegularMarketTime" = EXCLUDED."RegularMarketTime"
            """,
            ct);
    }
}
