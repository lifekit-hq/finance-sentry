namespace FinanceSentry.Integration;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.PortfolioConstruction.Domain.Ports;

/// <summary>
/// Spec 726 OQ-1: implements the Portfolio Construction module's <see cref="IHeldEquitySource"/> over Core's
/// brokerage holdings read, keeping single equities only (<c>STK</c>), the same filter the Radar universe uses.
/// </summary>
public sealed class BrokerageHeldEquityAdapter(IBrokerageHoldingsReader holdings) : IHeldEquitySource
{
    private const string EquityInstrumentType = "STK";

    public async Task<IReadOnlyList<string>> ListSymbolsAsync(Guid userId, CancellationToken ct)
    {
        var all = await holdings.GetHoldingsAsync(userId, ct);
        return all
            .Where(h => string.Equals(h.InstrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Symbol)
            .ToList();
    }
}
