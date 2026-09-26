using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Queries;

public sealed record GetTaxLotsQuery(Guid UserId) : IQuery<TaxLotsResponse>;

public sealed record TaxLotsResponse(
    string Provider,
    DateTime? SyncedAt,
    IReadOnlyList<TaxLotDto> Items,
    decimal TotalCostBasisUsd,
    decimal TotalUnrealizedPnlUsd);

/// <summary>
/// <see cref="BasisState"/> is "Verified", "Unverified" or "Unknown" (fs-688). Whenever it is not
/// "Verified", <see cref="CostBasisUsd"/>, <see cref="AverageCostUsd"/>, <see cref="UnrealizedPnlUsd"/>
/// and <see cref="UnrealizedPnlPercent"/> are null: the gate withholds gain/loss rather than state it
/// on an unverified or unknown basis.
/// </summary>
public sealed record TaxLotDto(
    string Symbol,
    string InstrumentType,
    decimal Quantity,
    decimal CurrentValueUsd,
    decimal? AverageCostUsd,
    decimal? CostBasisUsd,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPercent,
    DateTime? AcquiredAt,
    bool IsLongTerm,
    string BasisState);

public sealed class GetTaxLotsQueryHandler(
    IBrokerageHoldingRepository holdingRepository,
    IBrokerageTradeRepository tradeRepository,
    BrokerageCostBasisReconciler reconciler)
    : IQueryHandler<GetTaxLotsQuery, TaxLotsResponse>
{
    private static readonly TimeSpan LongTermThreshold = TimeSpan.FromDays(365);

    private readonly IBrokerageHoldingRepository _holdingRepository = holdingRepository;
    private readonly IBrokerageTradeRepository _tradeRepository = tradeRepository;
    private readonly BrokerageCostBasisReconciler _reconciler = reconciler;

    public async Task<TaxLotsResponse> Handle(GetTaxLotsQuery request, CancellationToken ct)
    {
        var holdings = await _holdingRepository.GetByUserIdAsync(request.UserId, ct);

        if (holdings.Count == 0)
            return new TaxLotsResponse("ibkr", null, [], 0m, 0m);

        var trades = await _tradeRepository.GetByUserIdAsync(request.UserId, ct);
        var now = DateTime.UtcNow;

        var items = holdings
            .Where(h => h.Quantity > 0m)
            .OrderByDescending(h => h.UsdValue)
            .Select(h =>
            {
                var reconciliation = _reconciler.Reconcile(h, trades);
                var verified = reconciliation.State == BasisState.Verified;

                decimal? costBasisUsd = verified ? h.CostBasisUsd : null;
                decimal? averageCostUsd = verified ? h.AverageCostUsd : null;
                decimal? unrealized = verified && costBasisUsd is decimal cb ? h.UsdValue - cb : null;
                decimal? unrealizedPct = verified && costBasisUsd is decimal cb2 && cb2 > 0m
                    ? Math.Round((h.UsdValue - cb2) / cb2 * 100m, 2)
                    : null;
                var isLongTerm = h.AcquiredAt is DateTime acq && (now - acq) >= LongTermThreshold;

                return new TaxLotDto(
                    Symbol: h.Symbol,
                    InstrumentType: h.InstrumentType,
                    Quantity: h.Quantity,
                    CurrentValueUsd: h.UsdValue,
                    AverageCostUsd: averageCostUsd,
                    CostBasisUsd: costBasisUsd,
                    UnrealizedPnlUsd: unrealized,
                    UnrealizedPnlPercent: unrealizedPct,
                    AcquiredAt: h.AcquiredAt,
                    IsLongTerm: isLongTerm,
                    BasisState: reconciliation.State.ToString());
            })
            .ToList();

        return new TaxLotsResponse(
            Provider: "ibkr",
            SyncedAt: holdings.Max(h => h.SyncedAt),
            Items: items,
            TotalCostBasisUsd: items.Sum(i => i.CostBasisUsd ?? 0m),
            TotalUnrealizedPnlUsd: items.Sum(i => i.UnrealizedPnlUsd ?? 0m));
    }
}
