namespace FinanceSentry.Integration;

using FinanceSentry.Modules.BrokerageSync.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Feature 421 - implements <see cref="IHoldingTaxLotsReader"/> over the BrokerageSync module's
/// published <see cref="ITaxLotReader"/> port (#673). Lives in Integration so Modules.Research
/// never references Modules.BrokerageSync directly.
/// </summary>
public sealed class HoldingTaxLotsAdapter(ITaxLotReader taxLots) : IHoldingTaxLotsReader
{
    public async Task<IReadOnlyList<DossierTaxLotEntry>?> GetForSymbolAsync(
        Guid userId, string symbol, CancellationToken ct = default)
    {
        var matching = await taxLots.ListForSymbolAsync(userId, symbol, ct);

        // Return null when the user has no position in this symbol.
        if (matching.Count == 0)
        {
            return null;
        }

        return matching
            .Select(i => new DossierTaxLotEntry(
                Quantity: i.Quantity,
                CurrentValueUsd: i.CurrentValueUsd,
                AverageCostUsd: i.AverageCostUsd,
                CostBasisUsd: i.CostBasisUsd,
                UnrealizedPnlUsd: i.UnrealizedPnlUsd,
                UnrealizedPnlPercent: i.UnrealizedPnlPercent,
                AcquiredAt: i.AcquiredAt,
                IsLongTerm: i.IsLongTerm))
            .ToList();
    }
}
