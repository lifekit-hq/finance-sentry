namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Modules.Wealth.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary><see cref="IBookCashHistoryReader"/> impl over the per-source rows of the net-worth snapshots.</summary>
public sealed class BookCashHistoryReader(INetWorthSnapshotRepository snapshots) : IBookCashHistoryReader
{
    private static readonly string[] BookSources = ["brokerage", "crypto"];

    public async Task<IReadOnlyList<DailyBookCash>> GetDailyAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var raw = await snapshots.GetByUserIdUnscopedAsync(userId, from, to, ct);
        var rows = await snapshots.GetSourcesUnscopedAsync(userId, [.. raw.Select(s => s.Id)], ct);
        var bySnapshot = rows.Where(r => BookSources.Contains(r.SourceId)).ToLookup(r => r.SnapshotId);

        var days = new List<DailyBookCash>(raw.Count);
        foreach (var snapshot in raw.OrderBy(s => s.SnapshotDate))
        {
            var book = bySnapshot[snapshot.Id].ToList();
            if (book.Count == 0 || book.Any(r => r.Total is null || r.Invested is null))
            {
                continue;
            }

            var total = book.Sum(r => r.Total!.Value);
            if (total <= 0m)
            {
                continue;
            }

            days.Add(new DailyBookCash(snapshot.SnapshotDate, total - book.Sum(r => r.Invested!.Value), total));
        }

        return days;
    }
}
