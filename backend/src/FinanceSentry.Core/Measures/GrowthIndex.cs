namespace FinanceSentry.Core.Measures;

/// <summary>
/// The fall of a flow-neutral growth index: <paramref name="Max"/> is the deepest fall from a prior high over
/// the whole series, <paramref name="Current"/> the fall at its last step. Both are positive fractions of the
/// peak (0.05 is a 5% fall), 0 for an index that never fell.
/// </summary>
public readonly record struct IndexDrawdown(decimal Max, decimal Current);

/// <summary>
/// The drawdown of a growth index chained from step returns (finance-framework §3.4, M8). The index opens at 1
/// before the first step, so a fall in the very first step counts, as in empyrical's <c>max_drawdown</c>. The
/// step returns must already be flow-neutral (time-weighted sub-periods, or the holdings-revaluation steps of
/// <c>BookDrawdownCalculator</c>); the index only chains and measures them. Pure: no I/O, no clock.
/// </summary>
public static class GrowthIndex
{
    /// <summary>The drawdown of the chained index, or null when there are no steps to chain.</summary>
    public static IndexDrawdown? Drawdown(IEnumerable<decimal> stepReturns)
    {
        var index = 1m;
        var peak = 1m;
        var max = 0m;
        var steps = 0;
        foreach (var stepReturn in stepReturns)
        {
            index *= 1m + stepReturn;
            peak = Math.Max(peak, index);
            max = Math.Max(max, 1m - (index / peak));
            steps++;
        }

        return steps == 0 ? null : new IndexDrawdown(max, 1m - (index / peak));
    }
}
