namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// Implements Radar's <see cref="IHoldingsHistorySource"/> from Risk's published
/// <see cref="IBrokerageHoldingsHistoryReader"/> port (symbol, quantity and USD value per position per
/// capture, brokerage sleeve only). Lives in the Integration layer so neither module references the other directly.
/// </summary>
public sealed class RadarHoldingsHistorySource(IBrokerageHoldingsHistoryReader holdings) : IHoldingsHistorySource
{
    public async Task<IReadOnlyList<HoldingsCapture>> GetBrokerageCapturesAsync(
        Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var captures = await holdings.ListSinceAsync(userId, since, ct);

        return captures
            .Select(c => new HoldingsCapture(
                c.CapturedAt,
                c.Positions.Select(p => new CapturedHolding(p.Symbol, p.Quantity, p.UsdValue)).ToList()))
            .ToList();
    }
}
