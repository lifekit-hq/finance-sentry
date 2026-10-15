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
/// </summary>
public readonly record struct CostEvent(
    DateOnly Date, CostEventKind Kind, decimal Quantity = 0m, decimal AmountUsd = 0m, decimal Ratio = 0m);

/// <summary>
/// M6 for one position. <paramref name="AverageCostUsd"/> is null when nothing is held.
/// <paramref name="UnrealizedUsd"/> = value − cost of what is held; <paramref name="RealizedUsd"/> = proceeds −
/// average cost of what was sold. Costs and proceeds are in USD at their trade dates and the value at the
/// valuation date's rate, so the currency effect sits inside the gains; the per-position split of it stays in
/// the Research module's <c>PositionReturnCalculator</c>.
/// </summary>
public sealed record GainVsCostResult(
    decimal QuantityHeld,
    decimal CostBasisUsd,
    decimal? AverageCostUsd,
    decimal ValueUsd,
    decimal UnrealizedUsd,
    decimal RealizedUsd,
    decimal IncomeUsd,
    decimal TotalGainUsd);

/// <summary>M6, gain vs cost by the standard average-cost method (finance-framework §3.4). Pure: no I/O, no clock.</summary>
public static class GainVsCost
{
    /// <param name="events">The position's history; applied in date order, same-day events in the order given.</param>
    /// <param name="valueUsd">The market value of what is held at the valuation date.</param>
    public static Measured<GainVsCostResult> Measure(IReadOnlyList<CostEvent> events, decimal valueUsd)
    {
        var quantity = 0m;
        var cost = 0m;
        var realized = 0m;
        var income = 0m;
        foreach (var e in events.OrderBy(e => e.Date))
        {
            if (e.Quantity < 0m || e.AmountUsd < 0m)
                return Measured<GainVsCostResult>.Fails(MeasureFailure.InvalidInput);

            switch (e.Kind)
            {
                case CostEventKind.Buy:
                    quantity += e.Quantity;
                    cost += e.AmountUsd;
                    break;
                case CostEventKind.Sell when e.Quantity > quantity:
                    return Measured<GainVsCostResult>.Fails(MeasureFailure.UnexplainedQuantity);
                case CostEventKind.Sell:
                    var soldCost = e.Quantity == quantity ? cost : cost * e.Quantity / quantity;
                    realized += e.AmountUsd - soldCost;
                    cost -= soldCost;
                    quantity -= e.Quantity;
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

        var unrealized = valueUsd - cost;
        return Measured<GainVsCostResult>.Of(new GainVsCostResult(
            quantity, cost, quantity == 0m ? null : cost / quantity, valueUsd,
            unrealized, realized, income, unrealized + realized + income));
    }
}
