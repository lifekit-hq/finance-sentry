using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;

namespace FinanceSentry.Modules.CryptoSync.Application.Queries;

public sealed record GetCryptoHoldingsQuery(Guid UserId) : IQuery<CryptoHoldingsResponse>;

/// <param name="Provider">
/// The single venue the holdings come from, <see cref="CryptoHoldingsResponse.MultipleProviders"/>
/// when they span several (each <see cref="CryptoHoldingDto"/> names its own venue), or
/// <see cref="CryptoHoldingsResponse.NoProvider"/> when there are none.
/// </param>
/// <param name="Headline">
/// <see cref="TotalUsdValue"/> as a typed <see cref="Core.Headlines.Headline"/> (USD): degraded while the
/// holdings are older than an hour, with the worst conversion of a fiat balance behind it.
/// </param>
public sealed record CryptoHoldingsResponse(
    string Provider,
    DateTime? SyncedAt,
    bool IsStale,
    IReadOnlyList<CryptoHoldingDto> Holdings,
    decimal TotalUsdValue,
    Headline? Headline = null)
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
    bool IsFiat = false,
    string? HiddenReason = null); // AssetVisibility.Empty / Small: display-only, the row still counts in the total and weights

public sealed class GetCryptoHoldingsQueryHandler : IQueryHandler<GetCryptoHoldingsQuery, CryptoHoldingsResponse>
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(1);

    private readonly ICryptoHoldingRepository _holdingRepository;
    private readonly IUserAssetVisibilityReader? _visibilityReader;
    private readonly IUserBaseCurrencyReader? _baseCurrencyReader;

    public GetCryptoHoldingsQueryHandler(
        ICryptoHoldingRepository holdingRepository,
        IUserAssetVisibilityReader? visibilityReader = null,
        IUserBaseCurrencyReader? baseCurrencyReader = null)
    {
        _holdingRepository = holdingRepository;
        _visibilityReader = visibilityReader;
        _baseCurrencyReader = baseCurrencyReader;
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

        var now = DateTime.UtcNow;
        var lastSyncedAt = holdings.Max(h => h.SyncedAt);
        var isStale = now - lastSyncedAt > StaleThreshold;

        // Rows are labelled, never dropped: the total below and the weights the page derives sum every row.
        var prefs = await AssetVisibility.ResolveInUsdAsync(_visibilityReader, _baseCurrencyReader, request.UserId, ct);
        var dtos = holdings
            .Select(h =>
            {
                var usdValue = CryptoHoldingValuation.UsdValue(h);
                return new CryptoHoldingDto(
                    h.Asset,
                    h.FreeQuantity,
                    h.LockedQuantity,
                    usdValue,
                    h.Provider,
                    h.IsFiat ? null : h.CostBasisUsd,
                    h.IsFiat ? null : h.AverageBuyPriceUsd,
                    h.IsFiat,
                    AssetVisibility.ClassifyPosition(h.FreeQuantity + h.LockedQuantity, usdValue, prefs));
            })
            .ToList();

        var provider = CryptoHoldingsResponse.ProviderOf(holdings.Select(h => h.Provider));
        var totalUsd = dtos.Sum(h => h.UsdValue);
        var signals = new HeadlineSignals
        {
            StaleSources = [.. holdings
                .GroupBy(h => h.Provider, StringComparer.OrdinalIgnoreCase)
                .Where(g => now - g.Max(h => h.SyncedAt) > StaleThreshold)
                .Select(g => g.Key.ToLowerInvariant())],
            Fx = holdings.Aggregate(FxStatus.Exact, (worst, h) => worst.Worst(CryptoHoldingValuation.Convert(h).Status)),
        };
        var scope = provider == CryptoHoldingsResponse.MultipleProviders
            ? HeadlineScope.Sleeve("crypto")
            : HeadlineScope.Source(provider);
        var asOf = new DateTimeOffset(DateTime.SpecifyKind(holdings.Min(h => h.SyncedAt), DateTimeKind.Utc));

        return new CryptoHoldingsResponse(
            Provider: provider,
            SyncedAt: lastSyncedAt,
            IsStale: isStale,
            Holdings: dtos,
            TotalUsdValue: totalUsd,
            Headline: HeadlineFactory.Value(totalUsd, scope, DateOnly.FromDateTime(now), signals, asOf));
    }
}
