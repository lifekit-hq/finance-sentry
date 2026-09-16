using System.Globalization;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

/// <summary>
/// Revolut X behind the same seam as Binance (#472). The API secret this adapter receives is the
/// user's Ed25519 private key (PEM); the API key is the id Revolut X issued for its public half.
/// </summary>
public sealed class RevolutXAdapter(
    RevolutXHttpClient httpClient,
    RevolutXHoldingsAggregator aggregator,
    ILogger<RevolutXAdapter> logger,
    IConfiguration configuration) : ICryptoExchangeAdapter
{
    private const decimal DefaultDustThresholdUsd = 0.01m;

    // The venue serves at most one week of fills per call.
    private const long WindowMs = 7L * 24 * 60 * 60 * 1000 - 2;

    // Half a year of windows: a walk that far behind finishes over the next runs.
    private const int MaxWindowsPerCall = 26;
    private const int MaxPagesPerWindow = 50;
    private const int PageLimit = 1000;
    private const string Usd = "USD";
    private const string BuySide = "buy";
    private const string SellSide = "sell";

    private static readonly string[] UsdStablecoins = ["USDC", "USDT"];

    private readonly decimal _dustThresholdUsd = decimal.TryParse(
        configuration["RevolutX:DustThresholdUsd"],
        System.Globalization.NumberStyles.Number,
        System.Globalization.CultureInfo.InvariantCulture,
        out var threshold) ? threshold : DefaultDustThresholdUsd;

    public string ExchangeName => CryptoExchangeProvider.RevolutX;

    public bool TradeHistoryStartsAtConnect => true;

    /// <summary>A live, signed <c>GET /balances</c>: proves the key pair is registered and usable.</summary>
    public async Task ValidateCredentialsAsync(string apiKey, string apiSecret, CancellationToken ct = default)
    {
        var credentials = RevolutXSigner.ParseCredentials(apiKey, apiSecret);
        await httpClient.GetBalancesAsync(credentials, ct);
    }

    public async Task<IReadOnlyList<CryptoAssetBalance>> GetHoldingsAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default)
    {
        var credentials = RevolutXSigner.ParseCredentials(apiKey, apiSecret);

        var balancesTask = httpClient.GetBalancesAsync(credentials, ct);
        var currenciesTask = httpClient.GetCurrenciesAsync(credentials, ct);
        var tickersTask = httpClient.GetTickersAsync(credentials, ct);
        await Task.WhenAll(balancesTask, currenciesTask, tickersTask);

        var snapshot = aggregator.Aggregate(
            balancesTask.Result,
            currenciesTask.Result,
            tickersTask.Result.Data,
            _dustThresholdUsd);

        if (snapshot.UnratedFiat.Count > 0)
        {
            logger.LogWarning(
                "Revolut X fiat balances valued 1:1 because no FX rate is known for them: {Currencies}",
                string.Join(", ", snapshot.UnratedFiat));
        }

        if (snapshot.UnpricedAssets.Count > 0)
        {
            logger.LogWarning(
                "Revolut X assets skipped because no USD-convertible ticker prices them: {Assets}",
                string.Join(", ", snapshot.UnpricedAssets));
        }

        return snapshot.Holdings;
    }

    /// <summary>
    /// The user's fills for <paramref name="asset"/> since the cursor (or since connect, for a
    /// never-walked holding), up to <see cref="CryptoTradeWalk.AsOf"/>. Revolut X serves one pair
    /// per call and at most a one-week window, so this walks every pair that sells
    /// <paramref name="asset"/> for a USD-valued quote, window by window; history before connect is
    /// out of reach, which is what the forward ledger is for.
    ///
    /// <list type="bullet">
    /// <item><b>Pairs.</b> Only pairs quoted in USD, a USD stablecoin (at par) or a fiat currency
    /// <see cref="CurrencyConverter"/> knows. A fill on any other pair (a crypto quote) cannot be
    /// priced in USD, so it is not returned: the ledger sees its quantity as unpriced.</item>
    /// <item><b>Windows.</b> Each window is asked for one millisecond wider on both sides and the
    /// fills filtered back to it, so a fill on a boundary is counted exactly once whether the
    /// venue treats the bounds as inclusive or exclusive.</item>
    /// <item><b>Failure.</b> Any call failing throws: the cursor stays where it was.</item>
    /// </list>
    /// </summary>
    public async Task<CryptoTradePage> GetTradesAsync(
        string apiKey,
        string apiSecret,
        string asset,
        string? cursor,
        CryptoTradeWalk walk,
        CancellationToken ct = default)
    {
        var credentials = RevolutXSigner.ParseCredentials(apiKey, apiSecret);
        var baseAsset = asset.Trim().ToUpperInvariant();

        var asOfMs = ToUnixMs(walk.AsOf);
        var startMs = RevolutXTradeCursor.Parse(cursor) is { } walkedTo
            ? walkedTo + 1
            : ToUnixMs(walk.TrackedSince);

        if (startMs > asOfMs)
        {
            return new CryptoTradePage([], cursor ?? RevolutXTradeCursor.Format(startMs - 1));
        }

        var pairs = await httpClient.GetPairsAsync(credentials, ct);
        var symbols = pairs.Values
            .Where(p => string.Equals(p.Base, baseAsset, StringComparison.OrdinalIgnoreCase)
                && IsUsdValued(p.Quote))
            .Select(p => $"{p.Base.ToUpperInvariant()}-{p.Quote.ToUpperInvariant()}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var fills = new Dictionary<string, CryptoTrade>(StringComparer.Ordinal);
        var windowStart = startMs;
        var windows = 0;
        var walkedToMs = startMs - 1;

        while (windowStart <= asOfMs && windows < MaxWindowsPerCall)
        {
            var windowEnd = Math.Min(windowStart + WindowMs - 1, asOfMs);

            foreach (var symbol in symbols)
            {
                await WalkWindowAsync(credentials, symbol, baseAsset, windowStart, windowEnd, fills, ct);
            }

            walkedToMs = windowEnd;
            windowStart = windowEnd + 1;
            windows++;
        }

        var trades = fills.Values
            .OrderBy(t => t.Timestamp)
            .ThenBy(t => t.TradeId, StringComparer.Ordinal)
            .ToList();

        return new CryptoTradePage(
            trades,
            RevolutXTradeCursor.Format(walkedToMs),
            IsComplete: walkedToMs >= asOfMs);
    }

    private async Task WalkWindowAsync(
        RevolutXCredentials credentials,
        string symbol,
        string baseAsset,
        long startMs,
        long endMs,
        Dictionary<string, CryptoTrade> fills,
        CancellationToken ct)
    {
        string? pageCursor = null;
        for (var page = 0; ; page++)
        {
            if (page == MaxPagesPerWindow)
            {
                throw new RevolutXException(
                    $"Revolut X returned more than {MaxPagesPerWindow} pages of {symbol} fills for one window.");
            }

            var response = await httpClient.GetPrivateTradesAsync(
                credentials, symbol, startMs - 1, endMs + 1, pageCursor, PageLimit, ct);

            foreach (var row in response.Data ?? [])
            {
                if (row.TradeTimestampMs < startMs || row.TradeTimestampMs > endMs)
                {
                    continue;
                }

                if (ToTrade(row, symbol, baseAsset) is { } trade)
                {
                    fills.TryAdd(trade.TradeId, trade);
                }
            }

            pageCursor = response.Metadata?.NextCursor;
            if (string.IsNullOrEmpty(pageCursor))
            {
                return;
            }
        }
    }

    private CryptoTrade? ToTrade(RevolutXTrade row, string symbol, string baseAsset)
    {
        var quantity = ParseAmount(row.Quantity);
        var price = ParseAmount(row.Price);
        var quote = row.PriceCurrency?.Trim().ToUpperInvariant();
        var side = row.Side?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(row.TradeId)
            || quantity is not > 0m
            || price is not > 0m
            || string.IsNullOrEmpty(quote)
            || !IsUsdValued(quote)
            || (side is not (BuySide or SellSide))
            || (row.QuantityCurrency is { } baseOnFill
                && !string.Equals(baseOnFill.Trim(), baseAsset, StringComparison.OrdinalIgnoreCase)))
        {
            // A fill the ledger cannot price is left out rather than guessed; its quantity then
            // reaches the ledger as unpriced.
            logger.LogWarning(
                "Revolut X fill {TradeId} on {Symbol} is unreadable and was skipped.", row.TradeId, symbol);
            return null;
        }

        var priceUsd = ToUsd(price.Value, quote);
        return new CryptoTrade(
            TradeId: row.TradeId,
            Asset: baseAsset,
            QuoteAsset: quote,
            Quantity: quantity.Value,
            PriceUsd: priceUsd,
            QuoteQuantityUsd: ToUsd(quantity.Value * price.Value, quote),
            IsBuyer: side == BuySide,
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(row.TradeTimestampMs).UtcDateTime);
    }

    /// <summary>USD as-is, a USD stablecoin at par, any other fiat through the FX table.</summary>
    private static bool IsUsdValued(string quote) =>
        string.Equals(quote, Usd, StringComparison.OrdinalIgnoreCase)
        || UsdStablecoins.Contains(quote, StringComparer.OrdinalIgnoreCase)
        || CurrencyConverter.IsKnown(quote);

    private static decimal ToUsd(decimal amount, string quote) =>
        string.Equals(quote, Usd, StringComparison.OrdinalIgnoreCase)
        || UsdStablecoins.Contains(quote, StringComparer.OrdinalIgnoreCase)
            ? amount
            : CurrencyConverter.ToUsd(amount, quote);

    private static long ToUnixMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    // A plain decimal string only: no thousands separators, which would silently turn "1,5" into 15.
    private static decimal? ParseAmount(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
