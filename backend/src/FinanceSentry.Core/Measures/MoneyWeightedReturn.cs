namespace FinanceSentry.Core.Measures;

/// <summary>Which method produced an M4 figure, and so how to read it.</summary>
public enum MoneyWeightedMethod
{
    /// <summary>XIRR, an annual rate: the window is one year or more.</summary>
    Xirr,

    /// <summary>Modified Dietz, the return over the window itself, never annualised: the window is under a year.</summary>
    ModifiedDietz,
}

/// <summary>M4: what the owner's money earned over the window, timing included, by <paramref name="Method"/>.</summary>
public sealed record MoneyWeightedResult(MoneyWeightedMethod Method, MeasuredRate Return);

/// <summary>
/// M4, money-weighted return (finance-framework §3.4): XIRR on an actual/365 day count for windows of one
/// year or more; Modified Dietz, not annualised, for shorter windows (GIPS 2.A.29, 2.A.12). Flows are placed
/// as in M3: an inflow at the start of its day (the previous close), an outflow at the end of its day. Income,
/// fees and taxes are already inside the values. Pure: no I/O, no clock.
/// </summary>
public static class MoneyWeightedReturn
{
    /// <param name="opening">The valuation the window opens on (the close before its first day).</param>
    /// <param name="closing">The valuation the window closes on.</param>
    /// <param name="flows">Classified flows; only external ones dated inside the window are used.</param>
    public static MoneyWeightedResult Measure(ValuePoint opening, ValuePoint closing, IReadOnlyList<ClassifiedFlow> flows)
    {
        var method = MeasureWindow.IsYearOrMore(opening.Date, closing.Date)
            ? MoneyWeightedMethod.Xirr
            : MoneyWeightedMethod.ModifiedDietz;
        var failure = Validate(opening, closing, flows);
        if (failure is not null)
            return new MoneyWeightedResult(method, MeasuredRate.Fails(failure.Value));

        var external = External(flows, opening.Date, closing.Date);
        var start = opening.ValueUsd!.Value;
        var end = closing.ValueUsd!.Value;
        var rate = method == MoneyWeightedMethod.Xirr
            ? XirrOf(opening.Date, start, closing.Date, end, external)
            : ModifiedDietzOf(opening.Date, start, closing.Date, end, external);

        return new MoneyWeightedResult(method, rate);
    }

    /// <summary>
    /// Modified Dietz over the window whatever its length: growth ÷ (V0 + Σ w_i·F_i), with
    /// w_i = (T − t_i) ÷ T the share of the window the flow was invested for. M4 uses it below one year.
    /// </summary>
    public static MeasuredRate ModifiedDietz(ValuePoint opening, ValuePoint closing, IReadOnlyList<ClassifiedFlow> flows)
    {
        var failure = Validate(opening, closing, flows);
        return failure is null
            ? ModifiedDietzOf(
                opening.Date, opening.ValueUsd!.Value, closing.Date, closing.ValueUsd!.Value,
                External(flows, opening.Date, closing.Date))
            : MeasuredRate.Fails(failure.Value);
    }

    private static MeasureFailure? Validate(ValuePoint opening, ValuePoint closing, IReadOnlyList<ClassifiedFlow> flows)
    {
        if (closing.Date <= opening.Date)
            return MeasureFailure.InsufficientData;
        if (MeasureWindow.HasNegativeAmount(flows))
            return MeasureFailure.InvalidInput;
        if (opening.ValueUsd is not decimal start || closing.ValueUsd is null)
            return MeasureFailure.MissingValue;

        return start switch
        {
            0m => MeasureFailure.ZeroBase,
            < 0m => MeasureFailure.NegativeBase,
            _ => null,
        };
    }

    private static List<ClassifiedFlow> External(IReadOnlyList<ClassifiedFlow> flows, DateOnly opening, DateOnly closing)
        => [.. MeasureWindow.FlowsWithin(flows, opening, closing)
            .Where(f => f.Class is FlowClass.ExternalInflow or FlowClass.ExternalOutflow)];

    private static MeasuredRate XirrOf(
        DateOnly opening, decimal start, DateOnly closing, decimal end, List<ClassifiedFlow> external)
    {
        var cashFlows = new List<DatedCashFlow> { new(opening, -(double)start) };
        cashFlows.AddRange(external
            .Select(f => f.Class == FlowClass.ExternalInflow
                ? new DatedCashFlow(f.Date.AddDays(-1), -(double)f.AmountUsd)
                : new DatedCashFlow(f.Date, (double)f.AmountUsd))
            .OrderBy(c => c.Date));
        cashFlows.Add(new DatedCashFlow(closing, (double)end));

        var outcome = Xirr.Solve(cashFlows);
        return outcome.Rate is double rate
            ? MeasuredRate.Of((decimal)rate)
            : MeasuredRate.Fails(outcome.Failure!.Value);
    }

    private static MeasuredRate ModifiedDietzOf(
        DateOnly opening, decimal start, DateOnly closing, decimal end, List<ClassifiedFlow> external)
    {
        decimal span = closing.DayNumber - opening.DayNumber;
        var net = 0m;
        var weighted = 0m;
        foreach (var flow in external)
        {
            var isInflow = flow.Class == FlowClass.ExternalInflow;
            var placedAt = isInflow ? flow.Date.AddDays(-1) : flow.Date;
            var weight = (span - (placedAt.DayNumber - opening.DayNumber)) / span;
            var signed = isInflow ? flow.AmountUsd : -flow.AmountUsd;
            net += signed;
            weighted += weight * signed;
        }

        var denominator = start + weighted;
        return denominator switch
        {
            0m => MeasuredRate.Fails(MeasureFailure.ZeroBase),
            < 0m => MeasuredRate.Fails(MeasureFailure.NegativeBase),
            _ => MeasuredRate.Of((end - start - net) / denominator),
        };
    }
}
