namespace FinanceSentry.Core.Measures;

/// <summary>
/// Beta of a scope against a benchmark: the sample covariance (n − 1) of the two monthly return series divided by
/// the benchmark's sample variance, over the calendar months both have a return for (matched by the month's closing
/// date). Both series are the M8 monthly time-weighted returns (<see cref="RiskResult.MonthlyReturns"/>), so no new
/// return is measured here and a deposit or withdrawal is never read as a swing. Fewer than two shared months, or a
/// benchmark that did not move, gives a typed reason, never a zero. Pure: no I/O, no clock.
/// </summary>
public static class MarketBeta
{
    private const int MinimumObservations = 2;

    public static MeasuredRate Measure(
        IReadOnlyList<SubPeriodReturn> scopeMonths, IReadOnlyList<SubPeriodReturn> benchmarkMonths, out int observations)
    {
        var benchmarkByClose = benchmarkMonths.ToDictionary(m => (m.To.Year, m.To.Month), m => m.Return);
        var pairs = scopeMonths
            .Where(m => benchmarkByClose.ContainsKey((m.To.Year, m.To.Month)))
            .Select(m => (Scope: m.Return, Benchmark: benchmarkByClose[(m.To.Year, m.To.Month)]))
            .ToList();
        observations = pairs.Count;
        if (pairs.Count < MinimumObservations)
            return MeasuredRate.Fails(MeasureFailure.NotEnoughHistory);

        var scopeMean = pairs.Average(p => p.Scope);
        var benchmarkMean = pairs.Average(p => p.Benchmark);
        var variance = pairs.Sum(p => (p.Benchmark - benchmarkMean) * (p.Benchmark - benchmarkMean));
        if (variance == 0m)
            return MeasuredRate.Fails(MeasureFailure.InvalidInput);

        var covariance = pairs.Sum(p => (p.Scope - scopeMean) * (p.Benchmark - benchmarkMean));
        return MeasuredRate.Of(covariance / variance);
    }
}
