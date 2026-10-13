namespace FinanceSentry.Modules.Research.Domain.Positions;

using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Where a holding's return came from, in USD (money-semantics §1, "Return decomposition"):
/// <see cref="UnrealizedUsd"/> is the open position's price gain at today's FX rate,
/// <see cref="RealizedUsd"/> the average-cost gain locked in by sells, <see cref="DividendsUsd"/> the
/// dividend income net of tax withheld, and <see cref="CurrencyUsd"/> what the move in the trade
/// currency's USD rate added to the open cost since the buys. A component is null when the ingested
/// ledger cannot support it; <see cref="TotalUsd"/> sums the non-null ones.
/// <see cref="Coverage"/>: <c>complete</c> = the fills explain the whole holding; <c>partial</c> = they
/// do not (history before the first ingested fill), so the unrealized figure is the holding's own
/// cost-basis gain and the realized and currency components are null; <c>unavailable</c> = no fills or dividends were
/// ingested for the symbol. <see cref="AverageCostNative"/> is the open average cost per unit in
/// <see cref="CostCurrency"/>, set only for complete coverage in a single trade currency.
/// </summary>
public sealed record PositionReturns(
    decimal? UnrealizedUsd,
    decimal? RealizedUsd,
    decimal? DividendsUsd,
    decimal? CurrencyUsd,
    decimal? TotalUsd,
    string Coverage,
    decimal? AverageCostNative,
    string? CostCurrency,
    int FillCount);

/// <summary>Pure average-cost walk over a symbol's ingested fills and dividend rows.</summary>
public static class PositionReturnCalculator
{
    public const string Complete = "complete";
    public const string Partial = "partial";
    public const string Unavailable = "unavailable";

    // Quantities are compared with this tolerance: fractional shares and certificates carry rounding.
    private const decimal QuantityTolerance = 0.0001m;
    private const int MoneyDecimals = 2;

    /// <param name="ledger">The symbol's fills and dividend rows, any order.</param>
    /// <param name="heldQuantity">Units held now.</param>
    /// <param name="currentValueUsd">The holding's value now, in USD.</param>
    /// <param name="costBasisUsd">The holding's own cost basis in USD, when the provider reports one.</param>
    /// <param name="usdPerUnitOn">USD per unit of a currency on a past date (historical FX).</param>
    /// <param name="usdPerUnitNow">USD per unit of a currency today.</param>
    public static PositionReturns Compute(
        PositionLedger ledger,
        decimal heldQuantity,
        decimal currentValueUsd,
        decimal? costBasisUsd,
        Func<string, DateOnly, decimal> usdPerUnitOn,
        Func<string, decimal> usdPerUnitNow)
    {
        var holdingGain = costBasisUsd is decimal basis ? currentValueUsd - basis : (decimal?)null;

        if (ledger.Fills.Count == 0 && ledger.Income.Count == 0)
        {
            return Result(holdingGain, null, null, null, Unavailable, null, null, 0);
        }

        var dividends = ledger.Income.Sum(i => i.Amount * usdPerUnitOn(i.Currency, DateOnly.FromDateTime(i.PaidAt)));

        var units = 0m;
        var costUsdThen = 0m;   // open cost at the FX rate of each buy
        var costUsdNow = 0m;    // the same native cost at today's FX rate
        var costNative = 0m;
        var realized = 0m;
        var explained = true;
        var currencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fill in ledger.Fills.OrderBy(f => f.TradedAt))
        {
            var day = DateOnly.FromDateTime(fill.TradedAt);
            var rateThen = usdPerUnitOn(fill.Currency, day);
            var commissionCurrency = fill.CommissionCurrency ?? fill.Currency;
            var commissionUsd = fill.Commission is decimal c ? c * usdPerUnitOn(commissionCurrency, day) : 0m;
            var commissionUsdNow = fill.Commission is decimal cn ? cn * usdPerUnitNow(commissionCurrency) : 0m;
            currencies.Add(fill.Currency);

            if (fill.Quantity > 0m)
            {
                var gross = fill.Quantity * fill.Price;
                units += fill.Quantity;
                costNative += gross;
                costUsdThen += gross * rateThen - commissionUsd;
                costUsdNow += gross * usdPerUnitNow(fill.Currency) - commissionUsdNow;
                continue;
            }

            var sold = -fill.Quantity;
            if (units <= QuantityTolerance)
            {
                explained = false;
                continue;
            }

            var matched = Math.Min(sold, units);
            var share = matched / units;
            var releasedThen = costUsdThen * share;

            // A disposal with no price (an Inzhur gift) releases its cost without realizing a gain.
            if (fill.Price > 0m)
            {
                realized += matched * fill.Price * rateThen + commissionUsd - releasedThen;
            }

            costUsdThen -= releasedThen;
            costUsdNow -= costUsdNow * share;
            costNative -= costNative * share;
            units -= matched;

            if (sold - matched > QuantityTolerance)
            {
                explained = false;
            }
        }

        var covers = explained && Math.Abs(units - heldQuantity) <= QuantityTolerance;
        if (!covers)
        {
            // Without the earlier fills the average cost of any sell is unknown, so only the holding's own gain and
            // the dividend rows stand.
            return Result(holdingGain, null, dividends, null, Partial, null, null, ledger.Fills.Count);
        }

        var unrealized = units > QuantityTolerance ? currentValueUsd - costUsdNow : 0m;
        var currencyEffect = units > QuantityTolerance ? costUsdNow - costUsdThen : 0m;
        var singleCurrency = currencies.Count == 1 ? currencies.First().ToUpperInvariant() : null;
        var averageCost = singleCurrency is not null && units > QuantityTolerance
            ? PriceRounding.Round(costNative / units)
            : (decimal?)null;

        return Result(unrealized, realized, dividends, currencyEffect, Complete, averageCost,
            averageCost is null ? null : singleCurrency, ledger.Fills.Count);
    }

    private static PositionReturns Result(
        decimal? unrealized, decimal? realized, decimal? dividends, decimal? currency, string coverage,
        decimal? averageCost, string? costCurrency, int fillCount)
    {
        decimal?[] parts = [unrealized, realized, dividends, currency];
        var total = parts.Any(p => p is not null) ? parts.Sum(p => p ?? 0m) : (decimal?)null;
        return new PositionReturns(
            Round(unrealized), Round(realized), Round(dividends), Round(currency), Round(total),
            coverage, averageCost, costCurrency, fillCount);
    }

    private static decimal? Round(decimal? value) => value is decimal v ? Math.Round(v, MoneyDecimals) : null;
}
