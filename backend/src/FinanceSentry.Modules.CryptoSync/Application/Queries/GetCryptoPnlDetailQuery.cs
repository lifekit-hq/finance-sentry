using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;

namespace FinanceSentry.Modules.CryptoSync.Application.Queries;

public sealed record GetCryptoPnlDetailQuery(Guid UserId) : IQuery<CryptoPnlDetailResponse>;

public sealed record CryptoPnlDetailResponse(
    string Provider,
    DateTime? SyncedAt,
    IReadOnlyList<CryptoPnlAssetDto> Items,
    decimal TotalUnrealizedPnlUsd,
    decimal TotalRealizedPnlUsd);

public sealed record CryptoPnlAssetDto(
    string Asset,
    decimal Quantity,
    decimal CurrentValueUsd,
    decimal? CostBasisUsd,
    decimal? AverageBuyPriceUsd,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPercent,
    decimal? RealizedPnlUsd,
    DateTime? LastTradeAt,
    int TradeCount,
    string Provider);

public sealed class GetCryptoPnlDetailQueryHandler(ICryptoHoldingRepository holdingRepository)
    : IQueryHandler<GetCryptoPnlDetailQuery, CryptoPnlDetailResponse>
{
    private readonly ICryptoHoldingRepository _holdingRepository = holdingRepository;

    public async Task<CryptoPnlDetailResponse> Handle(GetCryptoPnlDetailQuery request, CancellationToken ct)
    {
        // Venue fiat is cash: it has no cost basis and no P&L.
        var holdings = (await _holdingRepository.GetByUserIdAsync(request.UserId, ct))
            .Where(h => !h.IsFiat)
            .ToList();

        if (holdings.Count == 0)
        {
            return new CryptoPnlDetailResponse(CryptoHoldingsResponse.NoProvider, null, [], 0m, 0m);
        }

        var items = holdings
            .OrderByDescending(h => h.UsdValue)
            .Select(h =>
            {
                var quantity = h.FreeQuantity + h.LockedQuantity;
                decimal? unrealized = h.CostBasisUsd is decimal cb ? h.UsdValue - cb : null;
                decimal? unrealizedPct = h.CostBasisUsd is decimal cb2 && cb2 > 0m
                    ? Math.Round((h.UsdValue - cb2) / cb2 * 100m, 2)
                    : null;
                return new CryptoPnlAssetDto(
                    Asset: h.Asset,
                    Quantity: quantity,
                    CurrentValueUsd: h.UsdValue,
                    CostBasisUsd: h.CostBasisUsd,
                    AverageBuyPriceUsd: h.AverageBuyPriceUsd,
                    UnrealizedPnlUsd: unrealized,
                    UnrealizedPnlPercent: unrealizedPct,
                    RealizedPnlUsd: h.RealizedPnlUsd,
                    LastTradeAt: h.LastTradeAt,
                    TradeCount: h.TradeCount,
                    Provider: h.Provider);
            })
            .ToList();

        return new CryptoPnlDetailResponse(
            Provider: CryptoHoldingsResponse.ProviderOf(holdings.Select(h => h.Provider)),
            SyncedAt: holdings.Max(h => h.SyncedAt),
            Items: items,
            TotalUnrealizedPnlUsd: items.Sum(i => i.UnrealizedPnlUsd ?? 0m),
            TotalRealizedPnlUsd: items.Sum(i => i.RealizedPnlUsd ?? 0m));
    }
}
