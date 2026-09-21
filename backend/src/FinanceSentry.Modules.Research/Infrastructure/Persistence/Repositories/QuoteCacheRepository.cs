namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    /// Refreshes the cached quotes. On failure the shared <see cref="ResearchDbContext"/> is left
    /// exactly as it was found: the rows this call staged are rolled back out of the change tracker
    /// before the exception leaves.
    ///
    /// Why that matters (issue #626): this context is scoped, so it is the *caller's* unit of work —
    /// and every caller treats a quote refresh as best-effort and swallows the failure
    /// (<c>ThesisEventRecorder.TryGetPricesAsync</c>, the monitor, the snapshot jobs). Staged rows
    /// left behind survive the swallow, and the next unrelated <c>SaveChangesAsync</c> on the same
    /// context re-attempts them and throws — which is how a quote-cache write failure surfaced as
    /// <c>save_thesis</c> failing on every call while reads were fine. A cache is best-effort; its
    /// failure must not be contagious.
    /// </summary>
    public async Task UpsertManyAsync(IReadOnlyCollection<QuoteCacheEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var tickers = entries.Select(e => e.Ticker).ToArray();
        var existing = await db.QuoteCache
            .Where(q => tickers.Contains(q.Ticker))
            .ToDictionaryAsync(q => q.Ticker, ct);

        var staged = new List<EntityEntry<QuoteCacheEntry>>(entries.Count);

        foreach (var entry in entries)
        {
            if (existing.TryGetValue(entry.Ticker, out var row))
            {
                row.ResolvedTicker = entry.ResolvedTicker;
                row.Price = entry.Price;
                row.PreviousClose = entry.PreviousClose;
                row.Currency = entry.Currency;
                row.FetchedAt = entry.FetchedAt;
                row.MarketState = entry.MarketState;
                row.Session = entry.Session;
                row.IsStale = entry.IsStale;
                row.SourcePriceTime = entry.SourcePriceTime;
                row.RegularMarketTime = entry.RegularMarketTime;
                staged.Add(db.Entry(row));
            }
            else
            {
                staged.Add(db.QuoteCache.Add(entry));
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            Rollback(staged);
            throw;
        }
    }

    /// <summary>
    /// Undoes staged work: an inserted row is detached outright, an updated row has its mutated
    /// values restored from the snapshot EF took when it was loaded and is marked unchanged.
    /// </summary>
    private static void Rollback(List<EntityEntry<QuoteCacheEntry>> staged)
    {
        foreach (var entry in staged)
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
            else
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }
    }
}
