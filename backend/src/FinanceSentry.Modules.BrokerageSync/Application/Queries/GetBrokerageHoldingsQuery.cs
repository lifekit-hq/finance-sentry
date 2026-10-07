using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Queries;

public sealed record GetBrokerageHoldingsQuery(Guid UserId) : IQuery<BrokerageHoldingsResponse>;

/// <summary>
/// <see cref="Provider"/> names the broker each position comes from (<c>ibkr</c>, <c>inzhur</c>).
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
    string BasisState,
    string Provider = "ibkr");

/// <summary><see cref="Provider"/> is the one provider all positions come from, or <c>mixed</c>.</summary>
public sealed record BrokerageHoldingsResponse(
    string Provider,
    DateTime? SyncedAt,
    bool IsStale,
    DateOnly? FlexAsOfDate,
    IReadOnlyList<BrokeragePositionDto> Positions,
    decimal TotalUsdValue);

public sealed class GetBrokerageHoldingsQueryHandler(
    IBrokerageHoldingRepository holdingRepository,
    IBrokerageTradeRepository tradeRepository,
    BrokerageCostBasisReconciler reconciler)
    : IQueryHandler<GetBrokerageHoldingsQuery, BrokerageHoldingsResponse>
{
    private const int FlexStaleAfterDays = 4;
    private const string MixedProviders = "mixed";

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
                FlexAsOfDate: null,
                Positions: [],
                TotalUsdValue: 0m);
        }

        var trades = await _tradeRepository.GetByUserIdAsync(request.UserId, ct);
        var latestSyncedAt = holdings.Max(h => h.SyncedAt);
        var flexAsOf = holdings.Where(h => h.FlexAsOfDate.HasValue).Max(h => h.FlexAsOfDate);
        var now = DateTime.UtcNow;
        // Each provider is judged on its own cadence: Flex-sourced holdings are daily by nature (statement date,
        // calendar days), IBKR's live sync is intraday, Inzhur is read once a day.
        var isStale = holdings
            .GroupBy(h => h.Provider, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Max(h => h.FlexAsOfDate) is { } asOf
                ? asOf < DateOnly.FromDateTime(now).AddDays(-FlexStaleAfterDays)
                : BrokerageFreshness.IsStale(g.Key, g.Max(h => h.SyncedAt), now));
        var providers = holdings.Select(h => h.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
                    reconciliation.State.ToString(),
                    h.Provider);
            })
            .ToList();

        return new BrokerageHoldingsResponse(
            Provider: providers.Count == 1 ? providers[0] : MixedProviders,
            SyncedAt: latestSyncedAt,
            IsStale: isStale,
            FlexAsOfDate: flexAsOf,
            Positions: positions,
            TotalUsdValue: totalUsd);
    }
}
