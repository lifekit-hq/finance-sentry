namespace FinanceSentry.Modules.Research.Domain.Scoring;

/// <summary>Percentile ranking shared by the scan funnel's two stages (#558).</summary>
public static class PercentileRanks
{
    /// <summary>
    /// Inclusive percentile rank per ticker: share of the set's values at or below the ticker's own,
    /// scaled 0-100. A one-member set ranks 100 (trivially at the top of what exists).
    /// </summary>
    public static Dictionary<string, decimal> Of(IReadOnlyDictionary<string, decimal> valueByTicker)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (valueByTicker.Count == 0)
        {
            return result;
        }

        var values = valueByTicker.Values.OrderBy(v => v).ToList();
        foreach (var (ticker, value) in valueByTicker)
        {
            var atOrBelow = values.Count(v => v <= value);
            result[ticker] = Math.Round(100m * atOrBelow / values.Count, 2);
        }

        return result;
    }
}
