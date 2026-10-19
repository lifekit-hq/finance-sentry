using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;

/// <summary>
/// Pure-function transform that takes the raw responses from Binance's wallet
/// endpoints (Spot, Funding, Simple Earn flexible & locked) plus a price map
/// and produces a list of <see cref="CryptoAssetBalance"/> aggregated per asset.
///
/// No IO, no DI dependencies — the network orchestration lives in
/// <see cref="BinanceAdapter"/>. Keeping this class pure means each new source
/// (Futures, Margin, Options later) is a one-method addition with a tight unit
/// test, and the adapter's contract test stays focused on signing/wiring.
/// </summary>
public sealed class BinanceHoldingsAggregator
{
    private static readonly HashSet<string> Stablecoins = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDT", "USDC", "BUSD", "TUSD", "USDP", "DAI", "FDUSD",
    };

    public IReadOnlyList<CryptoAssetBalance> Aggregate(
        BinanceAccountResponse spot,
        IReadOnlyList<BinanceFundingAsset> funding,
        BinanceEarnPage<BinanceFlexibleEarnPosition> flexibleEarn,
        BinanceEarnPage<BinanceLockedEarnPosition> lockedEarn,
        IReadOnlyList<BinancePriceTicker> prices,
        decimal dustThresholdUsd)
    {
        var totals = new Dictionary<string, (decimal Free, decimal Locked)>(StringComparer.OrdinalIgnoreCase);

        AddSpotBalances(spot, totals);
        AddFundingBalances(funding, totals);
        AddFlexibleEarnBalances(flexibleEarn, totals);
        AddLockedEarnBalances(lockedEarn, totals);

        var priceMap = PriceMap(prices);

        var holdings = new List<CryptoAssetBalance>();
        foreach (var (asset, (free, locked)) in totals)
        {
            var total = free + locked;
            if (total <= 0m) { continue; }

            var usdValue = ComputeUsdValue(asset, total, priceMap);
            if (usdValue < dustThresholdUsd) { continue; }

            holdings.Add(new CryptoAssetBalance(asset, free, locked, usdValue));
        }

        return holdings;
    }

    /// <summary>Last price per symbol (<c>BTCUSDT</c> → price).</summary>
    public static bool IsStablecoin(string asset) => Stablecoins.Contains(asset);

    public static Dictionary<string, decimal> PriceMap(IReadOnlyList<BinancePriceTicker> prices) =>
        prices.ToDictionary(
            p => p.Symbol,
            p => decimal.Parse(p.Price, System.Globalization.CultureInfo.InvariantCulture),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>USD per unit of <paramref name="asset"/>, priced the way holdings are; null when no pair prices it.</summary>
    public static decimal? UnitPriceUsd(string asset, Dictionary<string, decimal> priceMap) =>
        ComputeUsdValue(asset, 1m, priceMap) is > 0m and var price ? price : null;

    // ── Per-source contributors ─────────────────────────────────────────────

    private static void AddSpotBalances(
        BinanceAccountResponse account,
        Dictionary<string, (decimal Free, decimal Locked)> totals)
    {
        foreach (var balance in account.Balances)
        {
            if (!decimal.TryParse(balance.Free, out var free)) { free = 0m; }
            if (!decimal.TryParse(balance.Locked, out var locked)) { locked = 0m; }
            Add(totals, balance.Asset, free, locked);
        }
    }

    private static void AddFundingBalances(
        IReadOnlyList<BinanceFundingAsset> funding,
        Dictionary<string, (decimal Free, decimal Locked)> totals)
    {
        foreach (var entry in funding)
        {
            if (!decimal.TryParse(entry.Free, out var free)) { free = 0m; }
            decimal locked = 0m;
            if (entry.Locked is not null) { decimal.TryParse(entry.Locked, out locked); }
            Add(totals, entry.Asset, free, locked);
        }
    }

    private static void AddFlexibleEarnBalances(
        BinanceEarnPage<BinanceFlexibleEarnPosition> page,
        Dictionary<string, (decimal Free, decimal Locked)> totals)
    {
        foreach (var position in page.Rows)
        {
            if (decimal.TryParse(position.TotalAmount, out var amount) && amount > 0m)
            {
                // Redeemable on demand → treated as "free".
                Add(totals, position.Asset, amount, 0m);
            }
        }
    }

    private static void AddLockedEarnBalances(
        BinanceEarnPage<BinanceLockedEarnPosition> page,
        Dictionary<string, (decimal Free, decimal Locked)> totals)
    {
        foreach (var position in page.Rows)
        {
            if (decimal.TryParse(position.Amount, out var amount) && amount > 0m)
            {
                // Cannot be moved until maturity → treated as "locked".
                Add(totals, position.Asset, 0m, amount);
            }
        }
    }

    // ── Mechanics ───────────────────────────────────────────────────────────

    private static void Add(
        Dictionary<string, (decimal Free, decimal Locked)> totals,
        string asset,
        decimal free,
        decimal locked)
    {
        if (totals.TryGetValue(asset, out var existing))
        {
            totals[asset] = (existing.Free + free, existing.Locked + locked);
        }
        else
        {
            totals[asset] = (free, locked);
        }
    }

    private static decimal ComputeUsdValue(
        string asset,
        decimal quantity,
        Dictionary<string, decimal> priceMap)
    {
        if (Stablecoins.Contains(asset))
        {
            return quantity;
        }

        var usdtPair = $"{asset}USDT";
        if (priceMap.TryGetValue(usdtPair, out var usdtPrice))
        {
            return quantity * usdtPrice;
        }

        var btcPair = $"{asset}BTC";
        if (priceMap.TryGetValue(btcPair, out var btcPrice) &&
            priceMap.TryGetValue("BTCUSDT", out var btcUsdtPrice))
        {
            return quantity * btcPrice * btcUsdtPrice;
        }

        return 0m;
    }
}
