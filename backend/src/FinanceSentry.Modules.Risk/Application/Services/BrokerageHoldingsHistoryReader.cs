namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;

/// <summary><see cref="IBrokerageHoldingsHistoryReader"/> impl over the internal holding-snapshot repository.</summary>
public sealed class BrokerageHoldingsHistoryReader(IHoldingSnapshotRepository snapshots) : IBrokerageHoldingsHistoryReader
{
    public async Task<IReadOnlyList<BrokerageHoldingsCapture>> ListSinceAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default)
    {
        var rows = await snapshots.ListSinceUnscopedAsync(userId, since, ct);

        return rows
            .Where(r => string.Equals(r.Sleeve, RiskSleeve.Brokerage, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.CapturedAt)
            .OrderBy(g => g.Key)
            .Select(g => new BrokerageHoldingsCapture(
                g.Key,
                g.Select(r => new BrokerageHoldingPosition(r.Symbol, r.Quantity, r.UsdValue)).ToList()))
            .ToList();
    }
}
