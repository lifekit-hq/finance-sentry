using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Queries;

public sealed record GetBrokerageHoldingsQuery(Guid UserId) : IQuery<BrokerageHoldingsResponse>;

/// <summary>
/// <see cref="BasisState"/> is "Verified", "Unverified" or "Unknown" (fs-688) — see
/// <c>BrokerageCostBasisReconciler</c>. <see cref="CostBasisUsd"/> and <see cref="AverageCostUsd"/> are
/// null whenever it is not "Verified".
/// </summary>
public sealed record BrokeragePositionDto(
    string Symbol,
    string InstrumentType,
    decimal Quantity,
    decimal UsdValue,
    decimal? CostBasisUsd,
    decimal? AverageCostUsd,
    string BasisState);

public sealed record BrokerageHoldingsResponse(
    string Provider,
    DateTime? SyncedAt,
    bool IsStale,
    IReadOnlyList<BrokeragePositionDto> Positions,
    decimal TotalUsdValue);

public sealed class GetBrokerageHoldingsQueryHandler(
    IBrokerageHoldingRepository holdingRepository,
    IBrokerageTradeRepository tradeRepository,
    BrokerageCostBasisReconciler reconciler)
    : IQueryHandler<GetBrokerageHoldingsQuery, BrokerageHoldingsResponse>
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(1);

    private readonly IBrokerageHoldingRepository _holdingRepository = holdingRepository;
    private readonly IBrokerageTradeRepository _tradeRepository = tradeRepository;
    private readonly BrokerageCostBasisReconciler _reconciler = reconciler;

    public async Task<BrokerageHoldingsResponse> Handle(
        GetBrokerageHoldingsQuery request, CancellationToken ct)
    {
        // Guard against any zero-quantity rows still in the DB from before reconcile ran.
        var holdings = (await _holdingRepository.GetByUserIdAsync(request.UserId, ct))
            .Where(h => h.Quantity != 0m)
            .ToList();

        if (holdings.Count == 0)
        {
            return new BrokerageHoldingsResponse(
                Provider: "ibkr",
                SyncedAt: null,
                IsStale: false,
                Positions: [],
                TotalUsdValue: 0m);
        }

        var trades = await _tradeRepository.GetByUserIdAsync(request.UserId, ct);
        var latestSyncedAt = holdings.Max(h => h.SyncedAt);
        var isStale = DateTime.UtcNow - latestSyncedAt > StaleThreshold;
        var totalUsd = holdings.Sum(h => h.UsdValue);

        var positions = holdings
            .Select(h =>
            {
                var reconciliation = _reconciler.Reconcile(h, trades);
                var verified = reconciliation.State == BasisState.Verified;

                return new BrokeragePositionDto(
                    h.Symbol,
                    h.InstrumentType,
                    h.Quantity,
                    h.UsdValue,
                    verified ? h.CostBasisUsd : null,
                    verified ? h.AverageCostUsd : null,
                    reconciliation.State.ToString());
            })
            .ToList();

        return new BrokerageHoldingsResponse(
            Provider: "ibkr",
            SyncedAt: latestSyncedAt,
            IsStale: isStale,
            Positions: positions,
            TotalUsdValue: totalUsd);
    }
}
