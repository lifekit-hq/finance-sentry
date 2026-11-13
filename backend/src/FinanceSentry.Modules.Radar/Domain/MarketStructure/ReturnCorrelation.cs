namespace FinanceSentry.Modules.Radar.Domain.MarketStructure;

using FinanceSentry.Core.Interfaces;

/// <summary>
/// Pearson correlation of daily returns over the trailing <see cref="WindowReturns"/> common trading days.
/// Pure maths over date-keyed adjusted closes: no I/O, no clock. Fewer than <see cref="MinObservations"/>
/// common returns, a missing series or a flat series is "not evaluable" with a reason, never a number.
/// </summary>
public static class ReturnCorrelation
{
    /// <summary>Trailing common daily returns the correlation is taken over (the 63-day window, 022 FR-001d).</summary>
    public const int WindowReturns = StructureWindows.Quarter;

    /// <summary>Common returns required before a figure is reported; below this the pair is not evaluable.</summary>
    public const int MinObservations = 40;

    public readonly record struct Outcome(decimal? Value, int Observations, string? Reason);

    /// <summary>Close-to-close return per date, keyed by the later date; non-positive closes are skipped.</summary>
    public static IReadOnlyDictionary<DateOnly, decimal> DailyReturns(IReadOnlyDictionary<DateOnly, decimal> closes)
    {
        var ordered = closes.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).ToList();
        var returns = new Dictionary<DateOnly, decimal>(Math.Max(ordered.Count - 1, 0));
        for (var i = 1; i < ordered.Count; i++)
        {
            returns[ordered[i].Key] = ordered[i].Value / ordered[i - 1].Value - 1m;
        }

        return returns;
    }

    /// <summary>
    /// Weight-averaged daily return of a book: on each date the average runs over the positions that have a
    /// return that date, weights renormalised. Positions are (return series, weight) pairs.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, decimal> WeightedBookReturns(
        IReadOnlyCollection<(IReadOnlyDictionary<DateOnly, decimal> Returns, decimal Weight)> positions)
    {
        var sums = new Dictionary<DateOnly, (decimal Weighted, decimal Weight)>();
        foreach (var (returns, weight) in positions)
        {
            if (weight <= 0)
            {
                continue;
            }

            foreach (var (date, value) in returns)
            {
                sums[date] = sums.TryGetValue(date, out var s)
                    ? (s.Weighted + value * weight, s.Weight + weight)
                    : (value * weight, weight);
            }
        }

        return sums.ToDictionary(kv => kv.Key, kv => kv.Value.Weighted / kv.Value.Weight);
    }

    /// <summary>Pearson correlation over the last <see cref="WindowReturns"/> returns the two series share.</summary>
    public static Outcome Pearson(
        IReadOnlyDictionary<DateOnly, decimal> returnsA,
        IReadOnlyDictionary<DateOnly, decimal> returnsB)
    {
        if (returnsA.Count == 0 || returnsB.Count == 0)
        {
            return new Outcome(null, 0, CorrelationReasons.NoBars);
        }

        var dates = returnsA.Keys.Where(returnsB.ContainsKey).OrderBy(d => d).TakeLast(WindowReturns).ToList();
        if (dates.Count < MinObservations)
        {
            return new Outcome(null, dates.Count, CorrelationReasons.InsufficientOverlap);
        }

        var xs = dates.Select(d => (double)returnsA[d]).ToArray();
        var ys = dates.Select(d => (double)returnsB[d]).ToArray();
        var meanX = xs.Average();
        var meanY = ys.Average();
        double cov = 0, varX = 0, varY = 0;
        for (var i = 0; i < xs.Length; i++)
        {
            var dx = xs[i] - meanX;
            var dy = ys[i] - meanY;
            cov += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        if (varX == 0 || varY == 0)
        {
            return new Outcome(null, dates.Count, CorrelationReasons.ZeroVariance);
        }

        var r = Math.Clamp(cov / Math.Sqrt(varX * varY), -1d, 1d);
        return new Outcome(Math.Round((decimal)r, 4), dates.Count, null);
    }
}
