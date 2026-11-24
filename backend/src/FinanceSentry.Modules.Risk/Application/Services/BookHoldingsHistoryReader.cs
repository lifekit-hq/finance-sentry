namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;

/// <summary><see cref="IBookHoldingsHistoryReader"/> impl over the internal holding-snapshot repository.</summary>
public sealed class BookHoldingsHistoryReader(IHoldingSnapshotRepository snapshots) : IBookHoldingsHistoryReader
{
    public async Task<IReadOnlyList<BookHoldingsCapture>> ListSinceAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default)
    {
        var rows = await snapshots.ListSinceUnscopedAsync(userId, since, ct);

        return rows
            .GroupBy(r => r.CapturedAt)
            .OrderBy(g => g.Key)
            .Select(g => new BookHoldingsCapture(
                g.Key,
                g.Select(r => new BookHoldingPosition(r.Symbol, r.Sleeve, r.UsdValue)).ToList()))
            .ToList();
    }
}
