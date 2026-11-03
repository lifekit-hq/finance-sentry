namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;

/// <summary>
/// Serves <see cref="IBrokeragePriceHistoryReader"/> from <c>holding_snapshots</c>. A day's price for a symbol is its
/// latest capture of that day (the same end-of-day rule as <see cref="HoldingSnapshotInvestedHistoryReader"/>).
/// </summary>
public sealed class HoldingSnapshotBrokeragePriceHistoryReader(IHoldingSnapshotRepository snapshots) : IBrokeragePriceHistoryReader
{
    public async Task<IReadOnlyList<DailySymbolPrice>> GetDailyPricesAsync(
        Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await snapshots.ListSinceUnscopedAsync(userId, since, ct);

        return rows
            .Where(r => r.Sleeve == RiskSleeve.Brokerage && r.Quantity > 0m)
            .GroupBy(r => (r.Symbol, Date: DateOnly.FromDateTime(r.CapturedAt.UtcDateTime)))
            .Select(g =>
            {
                var latest = g.OrderByDescending(r => r.CapturedAt).First();
                return new DailySymbolPrice(g.Key.Symbol, g.Key.Date, latest.UsdValue / latest.Quantity);
            })
            .OrderBy(p => p.Date)
            .ThenBy(p => p.Symbol, StringComparer.Ordinal)
            .ToList();
    }
}
