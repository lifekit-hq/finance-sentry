namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Ports;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

/// <summary><see cref="IInstrumentClassificationReader"/> over the instrument master.</summary>
public sealed class InstrumentClassificationReader(IBrokerageInstrumentRepository instruments)
    : IInstrumentClassificationReader
{
    public async Task<InstrumentTaxClass?> GetForSymbolAsync(
        Guid userId, string symbol, CancellationToken ct = default)
    {
        var matching = await instruments.GetBySymbolUnscopedAsync(userId, symbol, ct);
        var distinct = matching.Select(i => i.Classification).Distinct().ToList();

        // One agreed classification, or nothing: an unclassified or contradictory symbol must block the estimate.
        return distinct.Count == 1 ? distinct[0] switch
        {
            InstrumentClassification.OrdinaryShare => InstrumentTaxClass.OrdinaryShare,
            InstrumentClassification.OffshoreFund => InstrumentTaxClass.OffshoreFund,
            _ => null,
        } : null;
    }
}
