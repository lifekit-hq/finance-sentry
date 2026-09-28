namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BrokerageSync.Application.Queries;
using FinanceSentry.Modules.BrokerageSync.Domain.Ports;

/// <summary><see cref="ITaxLotReader"/> impl over BrokerageSync's own <see cref="GetTaxLotsQuery"/>.</summary>
public sealed class TaxLotReader(
    IQueryHandler<GetTaxLotsQuery, TaxLotsResponse> getTaxLots) : ITaxLotReader
{
    public async Task<IReadOnlyList<TaxLotReading>> ListForSymbolAsync(
        Guid userId, string symbol, CancellationToken ct = default)
    {
        var response = await getTaxLots.Handle(new GetTaxLotsQuery(userId), ct);
        return response.Items
            .Where(i => i.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Select(i => new TaxLotReading(
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
