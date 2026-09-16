using System.Globalization;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

/// <summary>What a Revolut X balances snapshot became, and what it deliberately did not.</summary>
public sealed record RevolutXHoldingsSnapshot(
    IReadOnlyList<CryptoAssetBalance> Holdings,
    IReadOnlyList<string> FiatBalancesExcluded,
    IReadOnlyList<string> UnpricedAssets);

/// <summary>
/// Pure transform from <c>GET /balances</c> + <c>GET /configuration/currencies</c> +
/// <c>GET /tickers</c> into per-asset crypto holdings valued in USD (#472).
///
/// <list type="bullet">
/// <item><b>Crypto only.</b> Fiat cash held on the venue is excluded here and reported in
/// <see cref="RevolutXHoldingsSnapshot.FiatBalancesExcluded"/>: it is neither a crypto holding nor
/// bank cash, and its representation is #472's follow-up PR.</item>
/// <item><b>USD at this boundary.</b> A pair quoted in USD is used as-is, a USD stablecoin quote is
/// taken at par, and any other fiat quote is converted with <see cref="CurrencyConverter"/> — the one
/// place the quote currency is still in scope. Native amounts are never summed across assets.</item>
/// <item><b>Decimal, invariant culture.</b> Every amount arrives as a string.</item>
/// </list>
/// </summary>
public sealed class RevolutXHoldingsAggregator
{
    private const string FiatAssetType = "fiat";
    private const string Usd = "USD";

    // Quotes that are a US dollar for valuation purposes, in preference order after USD itself.
    private static readonly string[] UsdStablecoins = ["USDC", "USDT"];

    public RevolutXHoldingsSnapshot Aggregate(
        IReadOnlyList<RevolutXBalance> balances,
        IReadOnlyDictionary<string, RevolutXCurrency> currencies,
        IReadOnlyList<RevolutXTicker> tickers,
        decimal dustThresholdUsd)
    {
        var prices = BuildPriceIndex(tickers);
        var holdings = new List<CryptoAssetBalance>();
        var fiat = new List<string>();
        var unpriced = new List<string>();

        foreach (var balance in balances)
        {
            var asset = balance.Currency?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(asset))
            {
                continue;
            }

            var (free, locked) = Quantities(balance);
            if (free + locked <= 0m)
            {
                continue;
            }

            if (IsFiat(asset, currencies))
            {
                fiat.Add(asset);
                continue;
            }

            var usdValue = UsdValue(asset, free + locked, prices);
            if (usdValue is null)
            {
                unpriced.Add(asset);
                continue;
            }

            if (usdValue.Value < dustThresholdUsd)
            {
                continue;
            }

            holdings.Add(new CryptoAssetBalance(asset, free, locked, Math.Round(usdValue.Value, 4)));
        }

        return new RevolutXHoldingsSnapshot(holdings, fiat, unpriced);
    }

    /// <summary>
    /// Free is <c>available</c>; locked is <c>reserved</c> plus <c>staked</c> — staked funds sit
    /// outside <c>total</c> (which is available + reserved) and are still the user's.
    /// </summary>
    private static (decimal Free, decimal Locked) Quantities(RevolutXBalance balance)
    {
        var staked = ParseAmount(balance.Staked) ?? 0m;

        if (ParseAmount(balance.Available) is { } available)
        {
            var reserved = ParseAmount(balance.Reserved) ?? 0m;
            return (available, reserved + staked);
        }

        return (ParseAmount(balance.Total) ?? 0m, staked);
    }

    private static bool IsFiat(string asset, IReadOnlyDictionary<string, RevolutXCurrency> currencies)
    {
        if (currencies.TryGetValue(asset, out var config) && !string.IsNullOrWhiteSpace(config.AssetType))
        {
            return string.Equals(config.AssetType, FiatAssetType, StringComparison.OrdinalIgnoreCase);
        }

        // Not in the venue's configuration: fall back to whether we hold an FX rate for it, which
        // only fiat currencies have.
        return CurrencyConverter.IsKnown(asset);
    }

    private static decimal? UsdValue(
        string asset,
        decimal quantity,
        IReadOnlyDictionary<string, Dictionary<string, decimal>> prices)
    {
        prices.TryGetValue(asset, out var quotes);

        if (quotes is not null && quotes.TryGetValue(Usd, out var usdPrice))
        {
            return quantity * usdPrice;
        }

        foreach (var stable in UsdStablecoins)
        {
            if (quotes is not null && quotes.TryGetValue(stable, out var stablePrice))
            {
                return quantity * stablePrice;
            }
        }

        if (quotes is not null)
        {
            foreach (var (quote, price) in quotes.OrderBy(q => q.Key, StringComparer.Ordinal))
            {
                if (CurrencyConverter.IsKnown(quote))
                {
                    return CurrencyConverter.ToUsd(quantity * price, quote);
                }
            }
        }

        // A USD stablecoin with no USD pair of its own is still a dollar.
        return UsdStablecoins.Contains(asset, StringComparer.Ordinal) ? quantity : null;
    }

    /// <summary>base → quote → price, from pairs written <c>BTC/USD</c>.</summary>
    private static Dictionary<string, Dictionary<string, decimal>> BuildPriceIndex(IReadOnlyList<RevolutXTicker> tickers)
    {
        var index = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in tickers)
        {
            var parts = ticker.Symbol?.Split('/');
            if (parts is not { Length: 2 } || parts[0].Length == 0 || parts[1].Length == 0)
            {
                continue;
            }

            var price = PositiveAmount(ticker.LastPrice) ?? PositiveAmount(ticker.Mid);
            if (price is null)
            {
                continue;
            }

            var baseAsset = parts[0].ToUpperInvariant();
            if (!index.TryGetValue(baseAsset, out var quotes))
            {
                quotes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                index[baseAsset] = quotes;
            }

            quotes[parts[1].ToUpperInvariant()] = price.Value;
        }

        return index;
    }

    private static decimal? PositiveAmount(string? value) =>
        ParseAmount(value) is { } parsed && parsed > 0m ? parsed : null;

    private static decimal? ParseAmount(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
