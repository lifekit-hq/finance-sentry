namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;

/// <summary>
/// Implements Radar's <see cref="IHoldingsHistorySource"/> from the Risk module's holding snapshots
/// (symbol, quantity and USD value per position per capture), brokerage sleeve only. Lives in the
/// Integration layer so neither module references the other directly. Reads unscoped: the weekly brief
/// job runs with no person in scope and keeps its own <c>UserId</c> predicate.
/// </summary>
public sealed class RadarHoldingsHistorySource(IHoldingSnapshotRepository snapshots) : IHoldingsHistorySource
{
    public async Task<IReadOnlyList<HoldingsCapture>> GetBrokerageCapturesAsync(
        Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rows = await snapshots.ListSinceUnscopedAsync(userId, since, ct);

        return rows
            .Where(r => string.Equals(r.Sleeve, RiskSleeve.Brokerage, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.CapturedAt)
            .OrderBy(g => g.Key)
            .Select(g => new HoldingsCapture(
                g.Key,
                g.Select(r => new CapturedHolding(r.Symbol, r.Quantity, r.UsdValue)).ToList()))
            .ToList();
    }
}
