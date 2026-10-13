namespace FinanceSentry.Integration;

using FinanceSentry.Modules.BrokerageSync.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Implements Research's <see cref="IPositionLedgerSource"/> over the BrokerageSync module's published
/// <see cref="IPositionLedgerReader"/> port. Lives in Integration so Modules.Research never references
/// Modules.BrokerageSync directly.
/// </summary>
public sealed class PositionLedgerAdapter(IPositionLedgerReader reader) : IPositionLedgerSource
{
    public async Task<PositionLedger> ReadForSymbolAsync(Guid userId, string symbol, CancellationToken ct = default)
    {
        var reading = await reader.ReadForSymbolAsync(userId, symbol, ct);
        return new PositionLedger(
            reading.Fills
                .Select(f => new LedgerFill(
                    f.Provider, f.TradedAt, f.Quantity, f.Price, f.Commission, f.CommissionCurrency, f.Currency))
                .ToList(),
            reading.Income
                .Select(i => new LedgerIncome(i.Provider, i.PaidAt, i.Amount, i.Currency))
                .ToList());
    }
}
