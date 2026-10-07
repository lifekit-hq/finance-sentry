using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

/// <summary>One Inzhur position as a brokerage holding row: UAH converted to USD once, here, at ingest.</summary>
public sealed record InzhurPosition(string Symbol, string InstrumentType, decimal Quantity, decimal UsdValue, decimal? AverageCostUsd);

/// <summary>
/// Maps a day's cabinet read onto brokerage holdings (money-semantics §1, "Inzhur"). Funds land as <c>REIT</c> (real
/// estate, never the equity universes that research/earnings/events build from <c>STK</c>), war bonds as <c>BOND</c>,
/// and the broker account's free + order-blocked UAH as one <c>CASH</c> row, the way IBKR's cash ledger lands.
/// </summary>
public static class InzhurHoldingsMapper
{
    public const string Provider = "inzhur";
    public const string FundInstrumentType = "REIT";
    public const string BondInstrumentType = "BOND";
    public const string CashInstrumentType = "CASH";
    public const string CashSymbol = "UAH Cash";

    private const string Currency = "UAH";
    private const string BondType = "bond";
    private const string FallbackSymbolPrefix = "INZHUR-";
    private const int SymbolMaxLength = 50;

    public static IReadOnlyList<InzhurPosition> Map(InzhurPortfolio portfolio)
    {
        var positions = portfolio.Assets
            .Select(MapAsset)
            .OfType<InzhurPosition>()
            // The holding row is unique per (user, symbol, provider): two lots of one asset become one row.
            .GroupBy(p => p.Symbol, StringComparer.Ordinal)
            .Select(Merge)
            .ToList();

        var account = portfolio.BrokerAccount;
        var cashUah = (account?.AvailableBalanceUah ?? 0m) + (account?.BlockedBalanceUah ?? 0m);
        if (cashUah != 0m)
            positions.Add(new InzhurPosition(CashSymbol, CashInstrumentType, cashUah, CurrencyConverter.ToUsd(cashUah, Currency), null));

        return positions;
    }

    public static string SymbolFor(InzhurAsset asset)
    {
        var name = FirstNonBlank(asset.Name, asset.Title, asset.Isin) ?? FallbackSymbolPrefix + asset.IdText;
        return name.Length <= SymbolMaxLength ? name : name[..SymbolMaxLength];
    }

    private static InzhurPosition? MapAsset(InzhurAsset asset)
    {
        var quantity = asset.Details?.CertificatesOwnedQuantity ?? asset.SecurityProperties?.AvailableQuantity ?? 0m;
        if (quantity <= 0m)
            return null;

        var price = asset.Prices?.SellUah ?? asset.Prices?.NavUah ?? 0m;
        var valueUah = asset.Details?.TotalAmountUah ?? quantity * price;
        var investedUah = asset.Details?.InvestedUah;

        return new InzhurPosition(
            SymbolFor(asset),
            string.Equals(asset.Type, BondType, StringComparison.OrdinalIgnoreCase) ? BondInstrumentType : FundInstrumentType,
            quantity,
            CurrencyConverter.ToUsd(valueUah, Currency),
            investedUah is > 0m ? CurrencyConverter.ToUsd(investedUah.Value, Currency) / quantity : null);
    }

    private static InzhurPosition Merge(IGrouping<string, InzhurPosition> lots)
    {
        if (lots.Count() == 1)
            return lots.First();

        var quantity = lots.Sum(p => p.Quantity);
        var cost = lots.All(p => p.AverageCostUsd.HasValue) ? lots.Sum(p => p.AverageCostUsd!.Value * p.Quantity) : (decimal?)null;
        return lots.First() with
        {
            Quantity = quantity,
            UsdValue = lots.Sum(p => p.UsdValue),
            AverageCostUsd = cost / quantity,
        };
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
