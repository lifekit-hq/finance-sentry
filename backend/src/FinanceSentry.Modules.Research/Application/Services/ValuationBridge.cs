namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Valuation;

/// <summary>
/// The enterprise-value -> equity bridge both valuation tools use (#1269), resolved once per target. Debt and
/// cash are in the reporting currency; <see cref="ToQuotedUnit"/> carries an amount to the unit the price is
/// quoted in (currency conversion and the minor-unit divisor, so GBp sits beside a pence price). A null piece is
/// missing - the flags say which - and is never replaced by a zero.
/// </summary>
public sealed record ValuationBridge(decimal? NetDebt, decimal? Shares, decimal? ToQuotedUnit)
{
    public static readonly ValuationBridge Empty = new(null, null, null);

    /// <summary>
    /// Net debt = debt less cash; shares = the reported count, else market cap / price (flag
    /// <c>SHARES_FROM_MARKET_CAP</c>); reporting -> quote currency through <see cref="CurrencyConverter"/>
    /// (<c>FX_CONVERTED</c>, or <c>FX_UNKNOWN</c> when a currency is not in its table).
    /// </summary>
    public static ValuationBridge Resolve(ValuationCurrentMetrics target, List<string> flags)
    {
        var inputs = target.Comps;
        decimal? netDebt = inputs?.TotalDebt is { } debt && inputs.TotalCash is { } cash ? debt - cash : null;
        if (netDebt is null)
        {
            flags.Add(CompsFlags.NetDebtUnavailable);
        }

        var (quoteCurrency, divisor) = MinorUnitCurrency.Normalize(target.Currency);

        decimal? shares = inputs?.SharesOutstanding is > 0m ? inputs.SharesOutstanding : null;
        if (shares is null && target.MarketCap is > 0m && target.Price is > 0m)
        {
            shares = target.MarketCap / (target.Price / divisor);
            flags.Add(CompsFlags.SharesFromMarketCap);
        }

        decimal? fx = 1m;
        var reporting = inputs?.FinancialCurrency?.Trim();
        if (!string.IsNullOrEmpty(reporting) && !string.IsNullOrEmpty(quoteCurrency)
            && !string.Equals(reporting, quoteCurrency, StringComparison.OrdinalIgnoreCase))
        {
            if (CurrencyConverter.IsKnown(reporting) && CurrencyConverter.IsKnown(quoteCurrency))
            {
                fx = CurrencyConverter.ToUsd(1m, reporting) / CurrencyConverter.ToUsd(1m, quoteCurrency);
                flags.Add(CompsFlags.FxConverted);
            }
            else
            {
                fx = null;
                flags.Add(CompsFlags.FxUnknown);
            }
        }

        return new ValuationBridge(netDebt, shares, fx is { } rate ? divisor * rate : null);
    }
}
