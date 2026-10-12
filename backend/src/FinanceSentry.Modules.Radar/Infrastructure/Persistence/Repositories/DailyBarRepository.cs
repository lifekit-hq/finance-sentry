namespace FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;

using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

public sealed class DailyBarRepository(RadarDbContext db) : IDailyBarRepository
{
    // One INSERT ... ON CONFLICT over the unique (Ticker, Date) index: Yahoo retroactively rescales AdjClose after
    // splits/dividends, so an existing row is overwritten in place (insert-only storage leaves a scale discontinuity
    // that corrupts returns and z-scores). The write is atomic, so two overlapping ingests update the same rows
    // instead of the loser failing on the index, and no read precedes it. It runs at once rather than at
    // SaveChanges, and a bar already tracked by this context is not refreshed. RETURNING (xmax = 0) is true only for
    // a freshly inserted row, which is what the added count reports.
    public async Task<int> UpsertRangeAsync(IReadOnlyCollection<DailyBar> bars, CancellationToken ct = default)
    {
        if (bars.Count == 0)
        {
            return 0;
        }

        // ON CONFLICT cannot touch one row twice in a statement, so a repeated (Ticker, Date) keeps its first bar.
        var incoming = bars
            .GroupBy(b => (b.Ticker, b.Date))
            .Select(g => g.First())
            .ToList();

        var tickers = incoming.Select(b => b.Ticker).ToArray();
        var dates = incoming.Select(b => b.Date).ToArray();
        var opens = incoming.Select(b => b.Open).ToArray();
        var highs = incoming.Select(b => b.High).ToArray();
        var lows = incoming.Select(b => b.Low).ToArray();
        var closes = incoming.Select(b => b.Close).ToArray();
        var adjCloses = incoming.Select(b => b.AdjClose).ToArray();
        var volumes = incoming.Select(b => b.Volume).ToArray();

        var inserted = await db.Database.SqlQuery<bool>(
            $"""
            INSERT INTO radar.daily_bars AS t
                ("Ticker", "Date", "Open", "High", "Low", "Close", "AdjClose", "Volume")
            SELECT * FROM unnest(
                {tickers}::varchar[], {dates}::date[], {opens}::numeric[], {highs}::numeric[], {lows}::numeric[],
                {closes}::numeric[], {adjCloses}::numeric[], {volumes}::bigint[])
            ON CONFLICT ("Ticker", "Date") DO UPDATE SET
                "Open" = EXCLUDED."Open",
                "High" = EXCLUDED."High",
                "Low" = EXCLUDED."Low",
                "Close" = EXCLUDED."Close",
                "AdjClose" = EXCLUDED."AdjClose",
                "Volume" = EXCLUDED."Volume"
            RETURNING (xmax = 0) AS "Value"
            """).ToListAsync(ct);

        return inserted.Count(isNew => isNew);
    }

    public async Task<IReadOnlyList<DailyBar>> GetSinceAsync(
        string ticker, DateOnly since, CancellationToken ct = default)
        => await db.DailyBars.AsNoTracking()
            .Where(b => b.Ticker == ticker && b.Date >= since)
            .OrderBy(b => b.Date)
            .ToListAsync(ct);

    public async Task<DateOnly?> GetLatestDateAsync(string ticker, CancellationToken ct = default)
    {
        var dates = await db.DailyBars.AsNoTracking()
            .Where(b => b.Ticker == ticker)
            .OrderByDescending(b => b.Date)
            .Select(b => b.Date)
            .Take(1)
            .ToListAsync(ct);

        return dates.Count > 0 ? dates[0] : null;
    }

    public async Task<IReadOnlyDictionary<string, DateOnly>> GetLatestDatesAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
    {
        if (tickers.Count == 0)
        {
            return new Dictionary<string, DateOnly>();
        }

        var rows = await db.DailyBars.AsNoTracking()
            .Where(b => tickers.Contains(b.Ticker))
            .GroupBy(b => b.Ticker)
            .Select(g => new { Ticker = g.Key, Latest = g.Max(b => b.Date) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Ticker, r => r.Latest);
    }
}
