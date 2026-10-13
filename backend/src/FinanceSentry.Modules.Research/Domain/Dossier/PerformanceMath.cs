namespace FinanceSentry.Modules.Research.Domain.Dossier;

/// <summary>
/// The dossier's one-line performance strip (#824): change over the past year, the 52-week closing high and
/// low and how far the last close sits below that high, against the S&amp;P 500's change over the same year.
/// All read from daily closes, so the high and low are closing values, not intraday extremes.
/// </summary>
public sealed record DossierPerformance(
    decimal? LastClose,
    decimal? YearChangePct,
    decimal? High52Week,
    decimal? Low52Week,
    decimal? FromHighPct,
    decimal? BenchmarkYearChangePct,
    string? Currency,
    DateOnly? AsOf);

public static class PerformanceMath
{
    private const int PercentScale = 100;
    private const int Decimals = 1;
    private const int PriceDecimals = 4;

    /// <summary>Percent change from the first to the last value, or null with fewer than two or a non-positive start.</summary>
    public static decimal? ChangePct(IReadOnlyList<decimal> closes)
        => closes.Count >= 2 && closes[0] > 0m
            ? Math.Round((closes[^1] / closes[0] - 1m) * PercentScale, Decimals)
            : null;

    public static DossierPerformance Build(
        IReadOnlyList<(DateOnly Date, decimal Close)> yearCloses,
        IReadOnlyList<decimal> benchmarkYearCloses,
        string? currency)
    {
        if (yearCloses.Count == 0)
        {
            return new DossierPerformance(null, null, null, null, null, ChangePct(benchmarkYearCloses), currency, null);
        }

        var values = yearCloses.Select(c => c.Close).ToList();
        var last = values[^1];
        var high = values.Max();
        var low = values.Min();
        var fromHigh = high > 0m ? Math.Round((last / high - 1m) * PercentScale, Decimals) : (decimal?)null;

        return new DossierPerformance(
            Math.Round(last, PriceDecimals),
            ChangePct(values),
            Math.Round(high, PriceDecimals),
            Math.Round(low, PriceDecimals),
            fromHigh,
            ChangePct(benchmarkYearCloses),
            currency,
            yearCloses[^1].Date);
    }
}
