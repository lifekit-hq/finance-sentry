namespace FinanceSentry.Core.Measures;

/// <summary>One linked sub-period: from the close of <paramref name="From"/> to the close of <paramref name="To"/>.</summary>
public sealed record SubPeriodReturn(DateOnly From, DateOnly To, decimal Return);

/// <summary>
/// M3 over the window from the close of <paramref name="From"/> to the close of <paramref name="To"/>.
/// <paramref name="Return"/> is cumulative over the window, never annualised. <paramref name="Periods"/> are
/// the linked sub-periods in order. <paramref name="MissingValueDays"/> are valuation days with no value,
/// bridged by a longer sub-period. <paramref name="UnexplainedDays"/> are closes at which value appeared from
/// an empty scope with no recorded inflow: those sub-periods are left out of the chain, and a figure with any
/// of them is degraded (reason <c>UNKNOWN_FLOW</c>, finance-framework §3.2 rule 4), never guessed.
/// </summary>
public sealed record TimeWeightedResult(
    MeasuredRate Return,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SubPeriodReturn> Periods,
    IReadOnlyList<DateOnly> MissingValueDays,
    IReadOnlyList<DateOnly> UnexplainedDays);

/// <summary>
/// M3, time-weighted return by chain-linking (finance-framework §3.4): Π(1 + r_t) − 1, where each
/// sub-period runs between two consecutive valued closes and r = (V_end + Out) / (V_start + In) − 1, with
/// the sub-period's external inflows at its start and external outflows at its end (rule 7, the Portfolio
/// Performance <c>ClientIndex</c> convention, re-implemented from the method). Income, fees and taxes are
/// already inside the values; internal flows are invisible. A sub-period whose start and end are both empty
/// (everything withdrawn, nothing deposited yet) carries no return and is skipped. Pure: no I/O, no clock.
/// </summary>
public static class TimeWeightedReturn
{
    /// <param name="values">The scope's closes over the window, any order; the earliest opens the window and
    /// the latest closes it. A daily series gives the daily chain-linking of M3.</param>
    /// <param name="flows">Classified flows; only external ones dated inside the window are used.</param>
    public static TimeWeightedResult Measure(IReadOnlyList<ValuePoint> values, IReadOnlyList<ClassifiedFlow> flows)
    {
        var points = values.OrderBy(v => v.Date).ToList();
        if (points.Count < 2)
        {
            var day = points.Count == 1 ? points[0].Date : default;
            return Failed(MeasureFailure.InsufficientData, day, day);
        }

        var from = points[0].Date;
        var to = points[^1].Date;
        var failure = Validate(points, flows);
        if (failure is not null)
            return Failed(failure.Value, from, to);

        var external = flows
            .Where(f => f.Class is FlowClass.ExternalInflow or FlowClass.ExternalOutflow)
            .ToList();
        var periods = new List<SubPeriodReturn>();
        var missing = new List<DateOnly>();
        var unexplained = new List<DateOnly>();
        var growth = 1m;
        var previous = points[0];
        foreach (var point in points.Skip(1))
        {
            if (point.ValueUsd is not decimal end)
            {
                missing.Add(point.Date);
                continue;
            }

            var inPeriod = external.Where(f => f.Date > previous.Date && f.Date <= point.Date).ToList();
            var start = previous.ValueUsd!.Value + ContributionsAndGrowth.Sum(inPeriod, FlowClass.ExternalInflow);
            end += ContributionsAndGrowth.Sum(inPeriod, FlowClass.ExternalOutflow);
            if (start == 0m)
            {
                if (end != 0m)
                    unexplained.Add(point.Date);
            }
            else
            {
                var periodReturn = (end / start) - 1m;
                periods.Add(new SubPeriodReturn(previous.Date, point.Date, periodReturn));
                growth *= 1m + periodReturn;
            }

            previous = point;
        }

        return new TimeWeightedResult(MeasuredRate.Of(growth - 1m), from, to, periods, missing, unexplained);
    }

    private static MeasureFailure? Validate(List<ValuePoint> points, IReadOnlyList<ClassifiedFlow> flows)
    {
        if (points.Select(p => p.Date).Distinct().Count() != points.Count || MeasureWindow.HasNegativeAmount(flows))
            return MeasureFailure.InvalidInput;
        if (points[0].ValueUsd is null || points[^1].ValueUsd is null)
            return MeasureFailure.MissingValue;
        if (points.Any(p => p.ValueUsd < 0m))
            return MeasureFailure.NegativeBase;

        return points[0].ValueUsd == 0m ? MeasureFailure.ZeroBase : null;
    }

    private static TimeWeightedResult Failed(MeasureFailure reason, DateOnly from, DateOnly to)
        => new(MeasuredRate.Fails(reason), from, to, [], [], []);
}
