namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// Close-price history for one Yahoo ticker over a chart range (the asset dossier's chart, performance
/// strip and momentum checks). Empty <see cref="PriceHistory.Closes"/> when the source has no series.
/// </summary>
public interface IPriceHistoryService
{
    Task<PriceHistory> GetAsync(string yahooTicker, string range, CancellationToken ct = default);
}

/// <summary>Closes oldest first, in <see cref="Currency"/> (the quote currency; null when the source omits it).</summary>
public sealed record PriceHistory(string? Currency, IReadOnlyList<DailyClose> Closes)
{
    public static PriceHistory Empty { get; } = new(null, []);
}

/// <summary>The chart ranges the dossier offers (#824's timeframe tabs).</summary>
public static class ChartRange
{
    public const string OneMonth = "1M";
    public const string SixMonths = "6M";
    public const string YearToDate = "YTD";
    public const string OneYear = "1Y";
    public const string FiveYears = "5Y";
    public const string Max = "MAX";

    public static IReadOnlyList<string> All { get; } = [OneMonth, SixMonths, YearToDate, OneYear, FiveYears, Max];

    /// <summary>The canonical range for <paramref name="value"/> (case-insensitive), or null when it is not one.</summary>
    public static string? Normalize(string? value)
        => All.FirstOrDefault(r => r.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
