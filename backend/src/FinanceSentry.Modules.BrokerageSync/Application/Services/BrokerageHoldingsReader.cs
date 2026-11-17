using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

public sealed class BrokerageHoldingsReader : IBrokerageHoldingsReader
{
    private readonly IBrokerageHoldingRepository _repository;
    private readonly IBrokerageTradeRepository _tradeRepository;
    private readonly IBrokerageInstrumentRepository _instrumentRepository;
    private readonly BrokerageCostBasisReconciler _reconciler;

    public BrokerageHoldingsReader(
        IBrokerageHoldingRepository repository,
        IBrokerageTradeRepository tradeRepository,
        IBrokerageInstrumentRepository instrumentRepository,
        BrokerageCostBasisReconciler reconciler)
    {
        _repository = repository;
        _tradeRepository = tradeRepository;
        _instrumentRepository = instrumentRepository;
        _reconciler = reconciler;
    }

    public async Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var holdings = await _repository.GetByUserIdUnscopedAsync(userId, ct);
        var trades = await _tradeRepository.GetByUserIdUnscopedAsync(userId, ct);
        var exchangeByInstrument = (await _instrumentRepository.GetByUserIdUnscopedAsync(userId, ct))
            .Where(i => i.ListingExchange is not null)
            .ToDictionary(i => i.Id, i => i.ListingExchange!);

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
                    reconciliation.State.ToString(),
                    h.FlexAsOfDate,
                    h.Currency,
                    h.NativeValue,
                    CurrencyConverter.Convert(1m, h.Currency).Status,
                    h.InstrumentId is { } instrumentId && exchangeByInstrument.TryGetValue(instrumentId, out var exchange) ? exchange : null);
            })
            .ToList();
    }
}
