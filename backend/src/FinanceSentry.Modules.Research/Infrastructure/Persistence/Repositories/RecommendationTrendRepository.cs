namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public class RecommendationTrendRepository(ResearchDbContext db) : IRecommendationTrendRepository
{
    private const int MaxMonths = 60;

    // One INSERT ... ON CONFLICT over the unique (Ticker, Period) index: the write is atomic, so two overlapping
    // ingests update the same rows instead of the loser failing on the index, and no read precedes it. It runs at
    // once rather than at SaveChanges, and a trend already tracked by this context is not refreshed. RETURNING
    // (xmax = 0) is true only for a freshly inserted row, which is what the inserted count reports.
    public async Task<int> UpsertAsync(
        IReadOnlyList<RecommendationTrend> trends, CancellationToken ct = default)
    {
        if (trends.Count == 0)
        {
            return 0;
        }

        // ON CONFLICT cannot touch one row twice in a statement, so a repeated (Ticker, Period) keeps its last trend.
        var incoming = trends
            .GroupBy(t => (t.Ticker, t.Period))
            .Select(g => g.Last())
            .ToList();

        var ids = incoming.Select(t => t.Id).ToArray();
        var tickers = incoming.Select(t => t.Ticker).ToArray();
        var periods = incoming.Select(t => t.Period).ToArray();
        var strongBuys = incoming.Select(t => t.StrongBuy).ToArray();
        var buys = incoming.Select(t => t.Buy).ToArray();
        var holds = incoming.Select(t => t.Hold).ToArray();
        var sells = incoming.Select(t => t.Sell).ToArray();
        var strongSells = incoming.Select(t => t.StrongSell).ToArray();
        var sources = incoming.Select(t => t.Source).ToArray();
        var ingestedAts = incoming.Select(t => t.IngestedAt).ToArray();

        var inserted = await db.Database.SqlQuery<bool>(
            $"""
            INSERT INTO research.recommendation_trends AS t
                ("Id", "Ticker", "Period", "StrongBuy", "Buy", "Hold", "Sell", "StrongSell", "Source", "IngestedAt")
            SELECT * FROM unnest(
                {ids}::uuid[], {tickers}::varchar[], {periods}::date[], {strongBuys}::integer[], {buys}::integer[],
                {holds}::integer[], {sells}::integer[], {strongSells}::integer[], {sources}::varchar[],
                {ingestedAts}::timestamptz[])
            ON CONFLICT ("Ticker", "Period") DO UPDATE SET
                "StrongBuy" = EXCLUDED."StrongBuy",
                "Buy" = EXCLUDED."Buy",
                "Hold" = EXCLUDED."Hold",
                "Sell" = EXCLUDED."Sell",
                "StrongSell" = EXCLUDED."StrongSell",
                "Source" = EXCLUDED."Source",
                "IngestedAt" = EXCLUDED."IngestedAt"
            RETURNING (xmax = 0) AS "Value"
            """).ToListAsync(ct);

        return inserted.Count(isNew => isNew);
    }

    public async Task<IReadOnlyList<RecommendationTrend>> GetLatestAsync(
        string ticker, int months, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        var effective = Math.Clamp(months, 1, MaxMonths);
        return await db.RecommendationTrends.AsNoTracking()
            .Where(t => t.Ticker == upper)
            .OrderByDescending(t => t.Period)
            .Take(effective)
            .ToListAsync(ct);
    }
}
