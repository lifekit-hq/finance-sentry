namespace FinanceSentry.Integration;

using FinanceSentry.Modules.BrokerageSync.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Implements Research's <see cref="IInstrumentTaxRegimeSource"/> over the BrokerageSync module's published
/// <see cref="IInstrumentClassificationReader"/> port, so Modules.Research never references Modules.BrokerageSync.
/// </summary>
public sealed class InstrumentTaxRegimeAdapter(IInstrumentClassificationReader classifications) : IInstrumentTaxRegimeSource
{
    public async Task<DisposalRegime?> GetForSymbolAsync(Guid userId, string symbol, CancellationToken ct = default)
        => await classifications.GetForSymbolAsync(userId, symbol, ct) switch
        {
            InstrumentTaxClass.OrdinaryShare => DisposalRegime.Cgt,
            InstrumentTaxClass.OffshoreFund => DisposalRegime.ExitTax,
            _ => null,
        };
}
