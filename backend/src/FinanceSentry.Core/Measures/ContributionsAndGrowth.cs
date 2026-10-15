namespace FinanceSentry.Core.Measures;

/// <summary>
/// M2: how much was put in, and how much it earned, in USD. <paramref name="ContributionsUsd"/> is external
/// inflows minus external outflows; <paramref name="GrowthUsd"/> is the value change minus that. Growth splits
/// into <paramref name="IncomeUsd"/>, <paramref name="FeesUsd"/> and <paramref name="TaxesUsd"/> (the latter
/// two as positive costs) and <paramref name="MarketUsd"/>, the remainder: price and currency moves.
/// </summary>
public sealed record ContributionsResult(
    decimal ChangeUsd,
    decimal ContributionsUsd,
    decimal GrowthUsd,
    decimal IncomeUsd,
    decimal FeesUsd,
    decimal TaxesUsd,
    decimal MarketUsd);

/// <summary>M2, contributions vs growth (finance-framework §3.4). Pure: no I/O, no clock.</summary>
public static class ContributionsAndGrowth
{
    /// <param name="startUsd">The scope's value at the window's opening.</param>
    /// <param name="endUsd">The scope's value at the window's closing.</param>
    /// <param name="windowFlows">The window's classified flows (<see cref="MeasureWindow.FlowsWithin"/>).
    /// Internal flows are invisible; income, fees and taxes are already inside the values and only split the
    /// growth.</param>
    public static Measured<ContributionsResult> Measure(
        decimal startUsd, decimal endUsd, IReadOnlyList<ClassifiedFlow> windowFlows)
    {
        if (MeasureWindow.HasNegativeAmount(windowFlows))
            return Measured<ContributionsResult>.Fails(MeasureFailure.InvalidInput);

        var change = endUsd - startUsd;
        var contributions = Sum(windowFlows, FlowClass.ExternalInflow) - Sum(windowFlows, FlowClass.ExternalOutflow);
        var growth = change - contributions;
        var income = Sum(windowFlows, FlowClass.Income);
        var fees = Sum(windowFlows, FlowClass.Fee);
        var taxes = Sum(windowFlows, FlowClass.Tax);

        return Measured<ContributionsResult>.Of(new ContributionsResult(
            change, contributions, growth, income, fees, taxes, growth - income + fees + taxes));
    }

    internal static decimal Sum(IEnumerable<ClassifiedFlow> flows, FlowClass flowClass)
        => flows.Where(f => f.Class == flowClass).Sum(f => f.AmountUsd);
}
