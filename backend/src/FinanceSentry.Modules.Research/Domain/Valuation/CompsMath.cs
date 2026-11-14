namespace FinanceSentry.Modules.Research.Domain.Valuation;

/// <summary>
/// Pure peer-statistics math for <c>get_comps_valuation</c> (#1269). Quartiles use linear interpolation
/// between closest ranks (the spreadsheet <c>PERCENTILE.INC</c> rule), the median is the 50th percentile,
/// and a target's percentile rank is the mid-rank over the usable peers (ties count half), so it never
/// reads 0 or 100 from a tie. Deterministic; nothing here reaches a network or the clock.
/// </summary>
public static class CompsMath
{
    /// <summary>Fewer usable peers than this makes a multiple not evaluable.</summary>
    public const int MinUsablePeers = 3;

    /// <summary>A trailing P/E above this is an outlier and stays out of the peer statistics.</summary>
    public const decimal PeOutlierThreshold = 100m;

    private const decimal LowerQuartile = 0.25m;
    private const decimal Median = 0.5m;
    private const decimal UpperQuartile = 0.75m;
    private const decimal PercentScale = 100m;
    private const decimal HalfWeight = 0.5m;

    /// <summary>Distribution of one metric over the usable peers.</summary>
    public sealed record Distribution(int Count, decimal Min, decimal P25, decimal Median, decimal P75, decimal Max);

    /// <summary>
    /// Min, 25th percentile, median, 75th percentile and max of <paramref name="values"/>, or null when
    /// there are fewer than <see cref="MinUsablePeers"/> of them.
    /// </summary>
    public static Distribution? Describe(IReadOnlyCollection<decimal> values)
    {
        if (values.Count < MinUsablePeers)
        {
            return null;
        }

        var sorted = values.Order().ToList();
        return new Distribution(
            sorted.Count,
            sorted[0],
            Percentile(sorted, LowerQuartile),
            Percentile(sorted, Median),
            Percentile(sorted, UpperQuartile),
            sorted[^1]);
    }

    /// <summary>Percentile (<paramref name="fraction"/> in 0-1) of an ascending, non-empty list by linear interpolation.</summary>
    public static decimal Percentile(IReadOnlyList<decimal> sorted, decimal fraction)
    {
        var position = fraction * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    /// <summary>
    /// Where <paramref name="value"/> sits among <paramref name="peers"/>, 0-100: the share of peers strictly
    /// below it plus half the share equal to it. Null when there are no peers.
    /// </summary>
    public static decimal? PercentileRank(decimal value, IReadOnlyCollection<decimal> peers)
    {
        if (peers.Count == 0)
        {
            return null;
        }

        var below = peers.Count(p => p < value);
        var equal = peers.Count(p => p == value);
        return PercentScale * (below + HalfWeight * equal) / peers.Count;
    }

    /// <summary>
    /// Per-share equity value implied by an enterprise value: (EV − net debt) ÷ shares. Null when there are
    /// no shares or the bridge leaves no equity (a negative equity value is not a share price).
    /// </summary>
    public static decimal? PerShareFromEnterpriseValue(decimal enterpriseValue, decimal netDebt, decimal shares)
    {
        var equity = enterpriseValue - netDebt;
        return shares > 0m && equity > 0m ? equity / shares : null;
    }
}
