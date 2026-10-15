namespace FinanceSentry.Core.Measures;

/// <summary>What happened to a position.</summary>
public enum CostEventKind
{
    /// <summary>Units bought; the amount is the full cost in USD at the trade date, fees included.</summary>
    Buy,

    /// <summary>Units sold; the amount is the net proceeds in USD at the trade date, fees deducted.</summary>
    Sell,

    /// <summary>A split: units multiply by the ratio (2 for 2-for-1) and the cost stays the same.</summary>
    Split,

    /// <summary>A dividend or interest paid on the position; the amount is in USD at the pay date.</summary>
    Income,
}

/// <summary>
/// One event in a position's history. <paramref name="Quantity"/> is for buys and sells,
/// <paramref name="AmountUsd"/> for buys, sells and income, <paramref name="Ratio"/> for splits; all positive.
/// <paramref name="AmountLocal"/> is a buy's or sell's amount in the position's own currency (fees included
/// or deducted as in <paramref name="AmountUsd"/>) and <paramref name="RateUsd"/> a sell's USD per unit of that
/// currency on the sale date; both are set on every buy and sell of a position held in a non-USD currency
/// (one currency per position) and on none of a USD position.
/// </summary>
public readonly record struct CostEvent(
    DateOnly Date,
    CostEventKind Kind,
    decimal Quantity = 0m,
    decimal AmountUsd = 0m,
    decimal Ratio = 0m,
    decimal? AmountLocal = null,
    decimal? RateUsd = null);

/// <summary>
/// The market value of what is held in the position's own currency, with the USD rate it is converted at
/// (USD per unit) on the valuation date.
/// </summary>
public readonly record struct LocalValue(decimal Value, decimal RateUsd);

/// <summary>Whether M6 could split its gains into price and currency effect.</summary>
public enum GainCurrencyBasis
{
    /// <summary>No local amounts were given (a USD position): the currency effect is 0 because it was not measured, not because it was found to be nil.</summary>
    UsdOnly,

    /// <summary>Local amounts were given: each gain is split into a price effect and a currency effect.</summary>
    LocalAmounts,
}

/// <summary>
/// M6 for one position. <paramref name="AverageCostUsd"/> is null when nothing is held.
/// <paramref name="UnrealizedUsd"/> = value - cost of what is held; <paramref name="RealizedUsd"/> = proceeds -
/// average cost of what was sold; costs and proceeds are in USD at their trade dates and the value at the
/// valuation date's rate. Each of the two gains is split: the price effect is the gain in the position's own
/// currency converted at the end (or sale) rate, the currency effect (<paramref name="UnrealizedCurrencyUsd"/>,
/// <paramref name="RealizedCurrencyUsd"/>) is the USD gain minus that. Income is at its pay date's rate and not
/// split. <paramref name="CurrencyBasis"/> says whether the split was measurable.
/// </summary>
public sealed record GainVsCostResult(
    decimal QuantityHeld,
    decimal CostBasisUsd,
    decimal? AverageCostUsd,
    decimal ValueUsd,
    decimal UnrealizedUsd,
    decimal RealizedUsd,
    decimal IncomeUsd,
    decimal TotalGainUsd,
    decimal UnrealizedCurrencyUsd,
    decimal RealizedCurrencyUsd,
    GainCurrencyBasis CurrencyBasis)
{
    /// <summary>The currency effect of both gains.</summary>
    public decimal CurrencyEffectUsd => UnrealizedCurrencyUsd + RealizedCurrencyUsd;

    /// <summary>The unrealized and realized gains without the currency effect.</summary>
    public decimal PriceEffectUsd => UnrealizedUsd + RealizedUsd - CurrencyEffectUsd;
}

/// <summary>M6, gain vs cost by the standard average-cost method (finance-framework §3.4). Pure: no I/O, no clock.</summary>
public static class GainVsCost
{
    // A split by a ratio like 1/3 leaves a quantity a few units short at the 28th digit; a sale of the units
    // the history shows is held must not read as unexplained, nor leave dust.
    private const decimal QuantityRelativeTolerance = 0.000000000001m;

    /// <param name="events">The position's history; applied in date order, same-day events in the order given.</param>
    /// <param name="valueUsd">The market value of what is held at the valuation date.</param>
    /// <param name="localValue">That value in the position's own currency; required when the events carry local amounts and units are held.</param>
    public static Measured<GainVsCostResult> Measure(
        IReadOnlyList<CostEvent> events, decimal valueUsd, LocalValue? localValue = null)
    {
        var isLocal = localValue is not null
            || events.Any(e => (e.Kind is CostEventKind.Buy or CostEventKind.Sell) && e.AmountLocal is not null);
        var quantity = 0m;
        var cost = 0m;
        var costLocal = 0m;
        var realized = 0m;
        var realizedCurrency = 0m;
        var income = 0m;
        foreach (var e in events.OrderBy(e => e.Date))
        {
            if (e.Quantity < 0m || e.AmountUsd < 0m || e.AmountLocal < 0m || e.RateUsd <= 0m)
                return Measured<GainVsCostResult>.Fails(MeasureFailure.InvalidInput);

            switch (e.Kind)
            {
                case CostEventKind.Buy:
                    if (isLocal && e.AmountLocal is null)
                        return Measured<GainVsCostResult>.Fails(MeasureFailure.InvalidInput);
                    quantity += e.Quantity;
                    cost += e.AmountUsd;
                    costLocal += e.AmountLocal ?? 0m;
                    break;
                case CostEventKind.Sell:
                    if (isLocal && (e.AmountLocal is null || e.RateUsd is null))
                        return Measured<GainVsCostResult>.Fails(MeasureFailure.InvalidInput);
                    var soldAll = Math.Abs(e.Quantity - quantity) <= quantity * QuantityRelativeTolerance;
                    if (e.Quantity > quantity && !soldAll)
                        return Measured<GainVsCostResult>.Fails(MeasureFailure.UnexplainedQuantity);
                    var soldCost = soldAll ? cost : cost * e.Quantity / quantity;
                    var soldCostLocal = soldAll ? costLocal : costLocal * e.Quantity / quantity;
                    var gain = e.AmountUsd - soldCost;
                    realized += gain;
                    if (isLocal)
                        realizedCurrency += gain - (((e.AmountLocal ?? 0m) - soldCostLocal) * (e.RateUsd ?? 0m));
                    cost -= soldCost;
                    costLocal -= soldCostLocal;
                    quantity = soldAll ? 0m : quantity - e.Quantity;
                    break;
                case CostEventKind.Split when e.Ratio <= 0m:
                    return Measured<GainVsCostResult>.Fails(MeasureFailure.InvalidInput);
                case CostEventKind.Split:
                    quantity *= e.Ratio;
                    break;
                case CostEventKind.Income:
                    income += e.AmountUsd;
                    break;
            }
        }

        if (isLocal && quantity > 0m && localValue is null)
            return Measured<GainVsCostResult>.Fails(MeasureFailure.InvalidInput);

        var unrealized = valueUsd - cost;
        var unrealizedCurrency = localValue is { } held
            ? unrealized - ((held.Value - costLocal) * held.RateUsd)
            : 0m;
        return Measured<GainVsCostResult>.Of(new GainVsCostResult(
            quantity, cost, quantity == 0m ? null : cost / quantity, valueUsd,
            unrealized, realized, income, unrealized + realized + income,
            unrealizedCurrency, realizedCurrency,
            isLocal ? GainCurrencyBasis.LocalAmounts : GainCurrencyBasis.UsdOnly));
    }
}
