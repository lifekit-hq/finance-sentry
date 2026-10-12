using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

/// <summary>
/// One Inzhur position as a brokerage holding row: the cabinet's amount kept as reported
/// (<see cref="NativeValue"/> in <see cref="Currency"/>) and converted to USD once, here, at ingest.
/// </summary>
public sealed record InzhurPosition(
    string Symbol, string InstrumentType, decimal Quantity, decimal UsdValue, decimal? AverageCostUsd, string Currency, decimal NativeValue);

/// <summary>
/// Maps a day's cabinet read onto brokerage holdings (money-semantics §1, "Inzhur"). Funds land as <c>REIT</c> (real
/// estate, never the equity universes that research/earnings/events build from <c>STK</c>), war bonds as <c>BOND</c>,
/// and the broker account's free + order-blocked UAH as one <c>CASH</c> row, the way IBKR's cash ledger lands. The
/// cabinet's money amounts are kopecks and are converted to hryvnia once, by <see cref="FromMinorUnits"/>.
/// </summary>
public static class InzhurHoldingsMapper
{
    public const string Provider = "inzhur";
    public const string FundInstrumentType = "REIT";
    public const string BondInstrumentType = "BOND";
    public const string CashInstrumentType = "CASH";
    public const string CashSymbol = "UAH Cash";

    /// <summary>What every Inzhur amount is denominated in unless the cabinet reports a currency itself.</summary>
    public const string DefaultCurrency = "UAH";

    /// <summary>The cabinet reports money in minor units (kopecks); its UI divides by this before showing any amount.</summary>
    public const decimal MinorUnitsPerMajor = 100m;

    private const string BondType = "bond";
    private const string FallbackSymbolPrefix = "INZHUR-";
    private const int SymbolMaxLength = 50;

    public static IReadOnlyList<InzhurPosition> Map(InzhurPortfolio portfolio)
    {
        var catalog = (portfolio.Catalog ?? [])
            .Where(c => c.AssetIdText.Length > 0)
            .GroupBy(c => c.AssetIdText, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var positions = portfolio.Assets
            .Select(a => MapAsset(a, catalog.GetValueOrDefault(a.IdText)))
            .OfType<InzhurPosition>()
            // The holding row is unique per (user, symbol, provider): two lots of one asset become one row.
            .GroupBy(p => p.Symbol, StringComparer.Ordinal)
            .Select(Merge)
            .ToList();

        var account = portfolio.BrokerAccount;
        var cashUah = FromMinorUnits(account?.AvailableBalanceUah) + FromMinorUnits(account?.BlockedBalanceUah);
        if (cashUah != 0m)
        {
            var currency = CurrencyFor(account?.Currency);
            positions.Add(new InzhurPosition(
                CashSymbol, CashInstrumentType, cashUah, CurrencyConverter.ToUsd(cashUah, currency), null, currency, cashUah));
        }

        return positions;
    }

    /// <summary>The currency the cabinet reported when a rate exists for it, else <see cref="DefaultCurrency"/>.</summary>
    public static string CurrencyFor(string? reported)
    {
        var code = reported?.Trim().ToUpperInvariant();
        return CurrencyConverter.IsKnown(code) ? code! : DefaultCurrency;
    }

    /// <summary>A cabinet money amount (kopecks) in major units (hryvnia); a missing amount is zero.</summary>
    public static decimal FromMinorUnits(decimal? minorUnits) => (minorUnits ?? 0m) / MinorUnitsPerMajor;

    public static string SymbolFor(InzhurAsset asset, InzhurCatalogAsset? catalog = null)
        => SymbolFor(
            asset.Type ?? catalog?.Type,
            FirstNonBlank(asset.Name, asset.Title, catalog?.Name),
            FirstNonBlank(asset.Isin, catalog?.Isin),
            asset.IdText);

    /// <summary>
    /// The one naming rule for an Inzhur instrument, shared by holdings and ledger rows so the cost-basis reconciler can
    /// match them by symbol: a war bond is its ISIN (the cabinet shows it as "ОВДП &lt;isin&gt;"), a fund its name; then
    /// whichever of the two exists, then <c>INZHUR-&lt;id&gt;</c>.
    /// </summary>
    public static string SymbolFor(string? type, string? name, string? isin, string? id)
    {
        var symbol = (IsBond(type) ? FirstNonBlank(isin, name) : FirstNonBlank(name, isin))
            ?? FallbackSymbolPrefix + id;
        return symbol.Length <= SymbolMaxLength ? symbol : symbol[..SymbolMaxLength];
    }

    public static bool IsBond(string? type) => string.Equals(type, BondType, StringComparison.OrdinalIgnoreCase);

    private static InzhurPosition? MapAsset(InzhurAsset asset, InzhurCatalogAsset? catalog)
    {
        var quantity = asset.Details?.CertificatesOwnedQuantity ?? asset.SecurityProperties?.AvailableQuantity ?? 0m;
        if (quantity <= 0m)
            return null;

        var price = asset.Prices?.SellUah ?? asset.Prices?.NavUah ?? 0m;
        var valueUah = asset.Details?.TotalAmountUah is { } total ? FromMinorUnits(total) : quantity * price;
        var investedUah = FromMinorUnits(asset.Details?.InvestedUah);
        var currency = CurrencyFor(asset.Currency);

        return new InzhurPosition(
            SymbolFor(asset, catalog),
            IsBond(asset.Type ?? catalog?.Type) ? BondInstrumentType : FundInstrumentType,
            quantity,
            CurrencyConverter.ToUsd(valueUah, currency),
            investedUah > 0m ? CurrencyConverter.ToUsd(investedUah, currency) / quantity : null,
            currency,
            valueUah);
    }

    private static InzhurPosition Merge(IGrouping<string, InzhurPosition> lots)
    {
        if (lots.Count() == 1)
            return lots.First();

        var quantity = lots.Sum(p => p.Quantity);
        // Lots of one asset share a currency; if the cabinet ever disagreed, only the USD figure is comparable.
        var sameCurrency = lots.Select(p => p.Currency).Distinct(StringComparer.Ordinal).Count() == 1;
        var cost = lots.All(p => p.AverageCostUsd.HasValue) ? lots.Sum(p => p.AverageCostUsd!.Value * p.Quantity) : (decimal?)null;
        return lots.First() with
        {
            Quantity = quantity,
            UsdValue = lots.Sum(p => p.UsdValue),
            Currency = sameCurrency ? lots.First().Currency : BrokerageHolding.DefaultCurrency,
            NativeValue = sameCurrency ? lots.Sum(p => p.NativeValue) : lots.Sum(p => p.UsdValue),
            AverageCostUsd = cost / quantity,
        };
    }

    internal static string? FirstNonBlank(params string?[] values)
        => values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
