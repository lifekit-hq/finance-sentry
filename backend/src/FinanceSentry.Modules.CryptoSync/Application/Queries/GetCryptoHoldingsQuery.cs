using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;

namespace FinanceSentry.Modules.CryptoSync.Application.Queries;

public sealed record GetCryptoHoldingsQuery(Guid UserId) : IQuery<CryptoHoldingsResponse>;

/// <param name="Provider">
/// The single venue the holdings come from, <see cref="CryptoHoldingsResponse.MultipleProviders"/>
/// when they span several (each <see cref="CryptoHoldingDto"/> names its own venue), or
/// <see cref="CryptoHoldingsResponse.NoProvider"/> when there are none.
/// </param>
public sealed record CryptoHoldingsResponse(
    string Provider,
    DateTime? SyncedAt,
    bool IsStale,
    IReadOnlyList<CryptoHoldingDto> Holdings,
    decimal TotalUsdValue)
{
    public const string MultipleProviders = "multiple";
    public const string NoProvider = "none";

    public static string ProviderOf(IEnumerable<string> providers)
    {
        var distinct = providers.Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count switch
        {
            0 => NoProvider,
            1 => distinct[0],
            _ => MultipleProviders,
        };
    }
}

public sealed record CryptoHoldingDto(
    string Asset,
    decimal FreeQuantity,
    decimal LockedQuantity,
    decimal UsdValue,
    string Provider,
    decimal? CostBasisUsd = null,
    decimal? AverageBuyPriceUsd = null,
    bool IsFiat = false);

public sealed class GetCryptoHoldingsQueryHandler : IQueryHandler<GetCryptoHoldingsQuery, CryptoHoldingsResponse>
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(1);

    private readonly ICryptoHoldingRepository _holdingRepository;

    public GetCryptoHoldingsQueryHandler(ICryptoHoldingRepository holdingRepository)
    {
        _holdingRepository = holdingRepository;
    }

    public async Task<CryptoHoldingsResponse> Handle(GetCryptoHoldingsQuery request, CancellationToken ct)
    {
        // Guard against any zero-balance rows still in the DB from before reconcile ran.
        var holdings = (await _holdingRepository.GetByUserIdAsync(request.UserId, ct))
            .Where(h => h.FreeQuantity + h.LockedQuantity != 0m)
            .ToList();

        if (holdings.Count == 0)
        {
            return new CryptoHoldingsResponse(
                Provider: CryptoHoldingsResponse.NoProvider,
                SyncedAt: null,
                IsStale: false,
                Holdings: [],
                TotalUsdValue: 0m);
        }

        var lastSyncedAt = holdings.Max(h => h.SyncedAt);
        var isStale = DateTime.UtcNow - lastSyncedAt > StaleThreshold;

        var dtos = holdings
            .Select(h => new CryptoHoldingDto(
                h.Asset,
                h.FreeQuantity,
                h.LockedQuantity,
                CryptoHoldingValuation.UsdValue(h),
                h.Provider,
                h.IsFiat ? null : h.CostBasisUsd,
                h.IsFiat ? null : h.AverageBuyPriceUsd,
                h.IsFiat))
            .ToList();

        return new CryptoHoldingsResponse(
            Provider: CryptoHoldingsResponse.ProviderOf(holdings.Select(h => h.Provider)),
            SyncedAt: lastSyncedAt,
            IsStale: isStale,
            Holdings: dtos,
            TotalUsdValue: dtos.Sum(h => h.UsdValue));
    }
}
