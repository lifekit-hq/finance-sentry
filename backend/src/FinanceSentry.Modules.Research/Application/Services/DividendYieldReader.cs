namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary><see cref="IDividendYieldReader"/> over the self-built <c>ValuationSnapshots</c> history.</summary>
public sealed class DividendYieldReader(ResearchDbContext db) : IDividendYieldReader
{
    public async Task<IReadOnlyDictionary<string, decimal?>> GetLatestAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
    {
        var upper = tickers.Select(t => t.Trim().ToUpperInvariant()).Where(t => t.Length > 0).Distinct().ToList();
        if (upper.Count == 0)
            return new Dictionary<string, decimal?>();

        var latest = await db.ValuationSnapshots.AsNoTracking()
            .Where(s => upper.Contains(s.Ticker))
            .GroupBy(s => s.Ticker)
            .Select(g => g.OrderByDescending(x => x.CapturedAt).Select(x => new { x.Ticker, x.DividendYield }).First())
            .ToListAsync(ct);

        return latest.ToDictionary(x => x.Ticker, x => x.DividendYield, StringComparer.Ordinal);
    }
}
