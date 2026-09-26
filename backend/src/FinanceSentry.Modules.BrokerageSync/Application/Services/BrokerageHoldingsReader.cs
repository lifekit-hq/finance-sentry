using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

public sealed class BrokerageHoldingsReader : IBrokerageHoldingsReader
{
    private readonly IBrokerageHoldingRepository _repository;
    private readonly IBrokerageTradeRepository _tradeRepository;
    private readonly BrokerageCostBasisReconciler _reconciler;

    public BrokerageHoldingsReader(
        IBrokerageHoldingRepository repository,
        IBrokerageTradeRepository tradeRepository,
        BrokerageCostBasisReconciler reconciler)
    {
        _repository = repository;
        _tradeRepository = tradeRepository;
        _reconciler = reconciler;
    }

    public async Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var holdings = await _repository.GetByUserIdAsync(userId, ct);
        var trades = await _tradeRepository.GetByUserIdAsync(userId, ct);

        return holdings
            .Select(h =>
            {
                var reconciliation = _reconciler.Reconcile(h, trades);
                var costBasisUsd = reconciliation.State == BasisState.Verified ? h.CostBasisUsd : null;

                return new BrokerageHoldingSummary(
                    h.Symbol,
                    h.InstrumentType,
                    h.Quantity,
                    h.UsdValue,
                    h.SyncedAt,
                    h.Provider,
                    costBasisUsd,
                    reconciliation.State.ToString());
            })
            .ToList();
    }
}
