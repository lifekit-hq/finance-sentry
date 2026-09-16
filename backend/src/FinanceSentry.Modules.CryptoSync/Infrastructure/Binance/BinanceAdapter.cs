using System.Globalization;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;

/// <summary>
/// HTTP orchestration only. Fans out the four wallet endpoints + price call in
/// parallel, tolerates permission failures on optional sources, and hands the
/// raw responses to <see cref="BinanceHoldingsAggregator"/> to produce the
/// per-asset balance list.
/// </summary>
public sealed class BinanceAdapter : ICryptoExchangeAdapter
{
    private readonly BinanceHttpClient _httpClient;
    private readonly BinanceHoldingsAggregator _aggregator;
    private readonly ILogger<BinanceAdapter> _logger;
    private readonly decimal _dustThresholdUsd;

    private static readonly string[] QuoteCandidates = ["USDT", "USDC", "FDUSD", "BUSD"];

    // Stablecoins are the quote side of every pair we walk; they have no USD-pair history of their own.
    private static readonly HashSet<string> NoTradeHistoryAssets = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDT", "USDC", "BUSD", "FDUSD", "DAI",
    };

    public string ExchangeName => CryptoExchangeProvider.Binance;

    public BinanceAdapter(
        BinanceHttpClient httpClient,
        BinanceHoldingsAggregator aggregator,
        ILogger<BinanceAdapter> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _aggregator = aggregator;
        _logger = logger;
        _dustThresholdUsd = decimal.TryParse(
            configuration["Binance:DustThresholdUsd"],
            out var threshold) ? threshold : 0.01m;
    }

    public async Task ValidateCredentialsAsync(string apiKey, string apiSecret, CancellationToken ct = default)
    {
        await _httpClient.GetAccountAsync(apiKey, apiSecret, ct);
    }

    public async Task<IReadOnlyList<CryptoAssetBalance>> GetHoldingsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default)
    {
        // Spot is the source of truth for credential health — fail loudly here.
        var spotTask = _httpClient.GetAccountAsync(apiKey, apiSecret, ct);
        var pricesTask = _httpClient.GetAllPricesAsync(ct);

        // Funding + Earn require additional permissions on the API key (Read-Only
        // is enough but the user may have scoped the key narrower). Treat as
        // best-effort: log and continue if any one of these is rejected.
        var fundingTask = SafeFetchAsync(
            () => _httpClient.GetFundingAssetsAsync(apiKey, apiSecret, ct),
            "Funding wallet",
            (IReadOnlyList<BinanceFundingAsset>)Array.Empty<BinanceFundingAsset>());

        var flexibleEarnTask = SafeFetchAsync(
            () => _httpClient.GetFlexibleEarnPositionsAsync(apiKey, apiSecret, ct),
            "Simple Earn (flexible)",
            new BinanceEarnPage<BinanceFlexibleEarnPosition>([], 0));

        var lockedEarnTask = SafeFetchAsync(
            () => _httpClient.GetLockedEarnPositionsAsync(apiKey, apiSecret, ct),
            "Simple Earn (locked)",
            new BinanceEarnPage<BinanceLockedEarnPosition>([], 0));

        await Task.WhenAll(spotTask, pricesTask, fundingTask, flexibleEarnTask, lockedEarnTask);

        return _aggregator.Aggregate(
            spotTask.Result,
            fundingTask.Result,
            flexibleEarnTask.Result,
            lockedEarnTask.Result,
            pricesTask.Result,
            _dustThresholdUsd);
    }

    public async Task<CryptoTradePage> GetTradesAsync(
        string apiKey,
        string apiSecret,
        string asset,
        string? cursor,
        CancellationToken ct = default)
    {
        const int pageLimit = 1000;
        const int maxPages = 20;

        if (NoTradeHistoryAssets.Contains(asset))
        {
            return new CryptoTradePage([], cursor);
        }

        var nextFromIds = BinanceTradeCursor.Parse(cursor, QuoteCandidates);
        var allTrades = new List<CryptoTrade>();

        foreach (var quote in QuoteCandidates)
        {
            var symbol = $"{asset.ToUpperInvariant()}{quote}";
            var fromId = nextFromIds.GetValueOrDefault(quote);
            for (var page = 0; page < maxPages; page++)
            {
                IReadOnlyList<BinanceTradeRow> rows;
                try
                {
                    rows = await _httpClient.GetMyTradesAsync(apiKey, apiSecret, symbol, fromId, pageLimit, ct);
                }
                catch (BinanceException ex)
                {
                    _logger.LogDebug(ex, "Binance trade history for {Symbol} unavailable (likely no such pair).", symbol);
                    break;
                }

                // fromId is inclusive, so a row below it was already counted by an earlier run.
                var fresh = rows.Where(r => r.Id >= fromId).ToList();
                foreach (var r in fresh)
                {
                    allTrades.Add(new CryptoTrade(
                        TradeId: r.Id.ToString(CultureInfo.InvariantCulture),
                        Asset: asset.ToUpperInvariant(),
                        QuoteAsset: quote,
                        Quantity: decimal.Parse(r.Quantity, CultureInfo.InvariantCulture),
                        PriceUsd: decimal.Parse(r.Price, CultureInfo.InvariantCulture),
                        QuoteQuantityUsd: decimal.Parse(r.QuoteQuantity, CultureInfo.InvariantCulture),
                        IsBuyer: r.IsBuyer,
                        Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(r.TimeMs).UtcDateTime));
                }

                if (fresh.Count > 0)
                {
                    // Ids are per symbol: the next run resumes this pair just past its last fill.
                    fromId = fresh.Max(r => r.Id) + 1;
                    nextFromIds[quote] = fromId;
                }

                if (rows.Count < pageLimit) break;
            }
        }

        var trades = allTrades
            .OrderBy(t => t.Timestamp)
            .ThenBy(t => t.QuoteAsset, StringComparer.Ordinal)
            .ThenBy(t => long.Parse(t.TradeId, CultureInfo.InvariantCulture))
            .ToList();

        return new CryptoTradePage(trades, BinanceTradeCursor.Format(nextFromIds));
    }

    private async Task<T> SafeFetchAsync<T>(Func<Task<T>> fetcher, string label, T fallback)
    {
        try
        {
            return await fetcher();
        }
        catch (BinanceException ex)
        {
            _logger.LogWarning(
                ex,
                "Skipping Binance source '{Source}' — request was rejected (likely missing API-key permission). Sync continues without this data.",
                label);
            return fallback;
        }
    }
}
