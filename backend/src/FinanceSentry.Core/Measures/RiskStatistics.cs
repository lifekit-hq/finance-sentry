namespace FinanceSentry.Core.Measures;

/// <summary>
/// M8 over the window from the first to the last valued close. <paramref name="MonthlyReturns"/> are the
/// time-weighted returns between consecutive calendar month-ends. Every rate is a fraction and is either a
/// number or the typed reason there is none; a series that is too short carries
/// <see cref="MeasureFailure.NotEnoughHistory"/>, never a zero. <paramref name="MissingValueDays"/> and
/// <paramref name="UnexplainedDays"/> are those of the whole-window time-weighted chain (M3): a figure with
/// any of them is degraded (reason <c>UNKNOWN_FLOW</c> for the latter), never guessed.
/// </summary>
/// <param name="Volatility">Standard deviation (n − 1) of the monthly returns × √12, over all of them.</param>
/// <param name="ThreeYearVolatility">The same over the last 36 monthly returns; needs at least 36.</param>
/// <param name="DownsideDeviation">√(mean of min(r, 0)² over all observations) × √12, target 0.</param>
/// <param name="MaxDrawdown">The deepest fall of the flow-neutral index from a prior high, as a positive fraction.</param>
public sealed record RiskResult(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SubPeriodReturn> MonthlyReturns,
    MeasuredRate Volatility,
    MeasuredRate ThreeYearVolatility,
    MeasuredRate DownsideDeviation,
    MeasuredRate MaxDrawdown,
    IReadOnlyList<DateOnly> MissingValueDays,
    IReadOnlyList<DateOnly> UnexplainedDays);

/// <summary>
/// M8, risk statistics (finance-framework §3.4), implemented from the empyrical definitions
/// (<c>annual_volatility</c>, <c>downside_risk</c>, <c>max_drawdown</c>) and checked against empyrical-reloaded
/// offline; LEAN's <c>Statistics.cs</c> is not the source. Volatility and downside deviation are annualised
/// with √12 because the observations are monthly. Downside deviation averages over <i>all</i> observations,
/// not only those below the target, so a mostly-up series is not made to look riskier. Flows are neutralised
/// by the time-weighted chain (M3), so a deposit or withdrawal is never read as a gain, a loss or a swing.
/// Pure: no I/O, no clock.
/// </summary>
public static class RiskStatistics
{
    /// <summary>GIPS 4.A.1.j: a three-year figure is shown only with 36 monthly returns behind it.</summary>
    public const int ThreeYearMonths = 36;

    private const int MonthsPerYear = 12;
    private const int MinimumObservations = 2;

    /// <param name="values">The scope's closes, any order. A month's return runs from the last valued close of
    /// the month before to the last valued close of that month, so the first, partial month gives none.</param>
    /// <param name="flows">Classified flows; only external ones dated inside the window are used.</param>
    public static RiskResult Measure(IReadOnlyList<ValuePoint> values, IReadOnlyList<ClassifiedFlow> flows)
    {
        var points = values.OrderBy(v => v.Date).ToList();
        var whole = TimeWeightedReturn.Measure(points, flows);
        if (whole.Return.Failure is { } failure)
            return Failed(failure, whole);

        var monthEnds = points
            .Where(p => p.ValueUsd is not null)
            .GroupBy(p => (p.Date.Year, p.Date.Month))
            .Select(g => g.Last())
            .ToList();
        var months = new List<SubPeriodReturn>();
        for (var i = 1; i < monthEnds.Count; i++)
        {
            var opening = monthEnds[i - 1];
            var closing = monthEnds[i];
            if (MonthIndex(closing.Date) - MonthIndex(opening.Date) != 1)
                return Failed(MeasureFailure.MissingValue, whole);

            var month = TimeWeightedReturn.Measure(
                [.. points.Where(p => p.Date >= opening.Date && p.Date <= closing.Date)],
                MeasureWindow.FlowsWithin(flows, opening.Date, closing.Date));
            if (month.Return.Value is not decimal monthReturn)
                return Failed(month.Return.Failure!.Value, whole);

            months.Add(new SubPeriodReturn(opening.Date, closing.Date, monthReturn));
        }

        var returns = months.Select(m => m.Return).ToList();
        return new RiskResult(
            whole.From,
            whole.To,
            months,
            Annualised(returns, StandardDeviation),
            returns.Count < ThreeYearMonths
                ? MeasuredRate.Fails(MeasureFailure.NotEnoughHistory)
                : Annualised(returns.TakeLast(ThreeYearMonths).ToList(), StandardDeviation),
            Annualised(returns, DownsideRootMeanSquare),
            GrowthIndex.Drawdown(whole.Periods.Select(p => p.Return)) is { } drawdown
                ? MeasuredRate.Of(drawdown.Max)
                : MeasuredRate.Fails(MeasureFailure.InsufficientData),
            whole.MissingValueDays,
            whole.UnexplainedDays);
    }

    private static int MonthIndex(DateOnly date) => (date.Year * MonthsPerYear) + date.Month;

    private static MeasuredRate Annualised(IReadOnlyList<decimal> returns, Func<IReadOnlyList<decimal>, decimal> statistic)
        => returns.Count < MinimumObservations
            ? MeasuredRate.Fails(MeasureFailure.NotEnoughHistory)
            : MeasuredRate.Of(statistic(returns) * (decimal)Math.Sqrt(MonthsPerYear));

    // Sample standard deviation (n − 1), numpy's std(ddof=1) as empyrical's annual_volatility uses it.
    private static decimal StandardDeviation(IReadOnlyList<decimal> returns)
    {
        var mean = returns.Average();
        var squares = returns.Sum(r => (r - mean) * (r - mean));
        return Root(squares / (returns.Count - 1));
    }

    // Root mean square of the shortfall below a target of 0 over every observation: an observation at or
    // above the target counts as 0 in the mean, as empyrical's downside_risk does.
    private static decimal DownsideRootMeanSquare(IReadOnlyList<decimal> returns)
        => Root(returns.Sum(r => Math.Min(r, 0m) * Math.Min(r, 0m)) / returns.Count);

    private static decimal Root(decimal value) => (decimal)Math.Sqrt((double)value);

    private static RiskResult Failed(MeasureFailure reason, TimeWeightedResult whole)
    {
        var failed = MeasuredRate.Fails(reason);
        return new RiskResult(whole.From, whole.To, [], failed, failed, failed, failed, [], []);
    }
}
