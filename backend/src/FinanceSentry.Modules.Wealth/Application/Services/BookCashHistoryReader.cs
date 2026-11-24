namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Modules.Wealth.Domain;
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
        var withSources = SnapshotSources.OfEach(
            raw, await snapshots.GetSourcesUnscopedAsync(userId, [.. raw.Select(s => s.Id)], ct));

        var days = new List<DailyBookCash>(raw.Count);
        foreach (var (snapshot, sources) in withSources.OrderBy(x => x.Snapshot.SnapshotDate))
        {
            var book = sources.Sources.Where(s => BookSources.Contains(s.SourceId)).ToList();
            // A carried-forward sleeve's row is not what the snapshot counted, and a sleeve with no row of its own has
            // no split (SnapshotSources always lists both): either way the day is left out, never a one-source book.
            if (book.Any(s => s.IsStale || s.Total is null || s.Invested is null))
            {
                continue;
            }

            var total = book.Sum(s => s.Total!.Value);
            if (total <= 0m)
            {
                continue;
            }

            days.Add(new DailyBookCash(snapshot.SnapshotDate, total - book.Sum(s => s.Invested!.Value), total));
        }

        return days;
    }
}
