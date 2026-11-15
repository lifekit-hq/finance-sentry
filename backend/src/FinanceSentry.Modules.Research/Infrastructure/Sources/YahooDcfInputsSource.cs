namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Globalization;
using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Infrastructure.Caching;
using Microsoft.Extensions.Logging;

/// <summary>
/// The DCF's statement history from Yahoo's fundamentals timeseries (#1269): the same key-less endpoint and HTTP
/// client the fundamentals chain's Yahoo source reads, asked for the income-statement and cash-flow lines the chain
/// does not carry (tax provision, pretax income, interest expense, depreciation and amortization, capital
/// expenditure, change in working capital). One provider and one annual basis, so no ratio mixes sources. Quarterly
/// revenue is read only to build the trailing-twelve-month base year. Results (and "no data") are cached per ticker
/// for 12 hours; a failure is not cached.
/// </summary>
public sealed class YahooDcfInputsSource(
    IHttpClientFactory httpFactory,
    TimeProvider clock,
    ILogger<YahooDcfInputsSource> logger) : IDcfInputsSource, IDisposable
{
    private const string AnnualPrefix = "annual";
    private const string QuarterlyPrefix = "quarterly";
    private const string AnnualPeriodType = "12M";
    private const string QuarterlyPeriodType = "3M";
    private const int HistoryYears = 7;
    private const int MaxAnnualPeriods = 6;
    private const int QuartersInYear = 4;
    private const int TtmWindowMinDays = 240;
    private const int TtmWindowMaxDays = 300;
    private const int MaxCachedTickers = 5_000;

    private static readonly TimeSpan ResultTtl = TimeSpan.FromHours(12);

    private static readonly string[] AnnualSeries =
    [
        "TotalRevenue", "OperatingIncome", "TaxProvision", "PretaxIncome", "InterestExpense",
        "DepreciationAndAmortization", "CapitalExpenditure", "ChangeInWorkingCapital",
    ];

    private static readonly string TypeQuery = string.Join(
        ',', AnnualSeries.Select(s => AnnualPrefix + s).Append(QuarterlyPrefix + "TotalRevenue"));

    private readonly TtlCache<DcfInputs?> cache = new(clock, ResultTtl, MaxCachedTickers);

    public Task<DcfInputs?> GetAsync(string ticker, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        return upper.Length == 0
            ? Task.FromResult<DcfInputs?>(null)
            : cache.GetOrFetchAsync(upper, token => QueryAsync(upper, token), ct);
    }

    private async Task<(DcfInputs? Value, bool Cacheable)> QueryAsync(string upper, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var symbol = Uri.EscapeDataString(upper);
        var url = $"https://query1.finance.yahoo.com/ws/fundamentals-timeseries/v1/finance/timeseries/{symbol}"
            + $"?symbol={symbol}&type={TypeQuery}"
            + $"&period1={now.AddYears(-HistoryYears).ToUnixTimeSeconds()}&period2={now.ToUnixTimeSeconds()}";

        try
        {
            var client = httpFactory.CreateClient(YahooFundamentalsSource.HttpClientName);
            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return (Parse(json), true);
        }
        // An HttpClient timeout is a TaskCanceledException too: only the caller's own cancellation propagates.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Yahoo DCF inputs fetch failed for {Ticker}", upper);
            return (null, false);
        }
    }

    /// <summary>
    /// Parses a timeseries response. Public + static so the contract test can assert the JSON shape against a
    /// recorded fixture. A point that is null, malformed or of the wrong period length is skipped, never zeroed;
    /// a period with only some lines keeps the others null. Returns <c>null</c> when there is no annual period.
    /// </summary>
    public static DcfInputs? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("timeseries", out var timeseries) ||
            !timeseries.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var annual = new Dictionary<DateOnly, Dictionary<string, decimal>>();
        var quarterlyRevenue = new Dictionary<DateOnly, decimal>();
        foreach (var series in results.EnumerateArray())
        {
            var type = SeriesType(series);
            if (type is null || !series.TryGetProperty(type, out var points) || points.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (type.StartsWith(AnnualPrefix, StringComparison.Ordinal))
            {
                var name = type[AnnualPrefix.Length..];
                foreach (var (end, value) in ReadPoints(points, AnnualPeriodType))
                {
                    if (!annual.TryGetValue(end, out var period))
                    {
                        annual[end] = period = new Dictionary<string, decimal>(StringComparer.Ordinal);
                    }

                    period[name] = value;
                }
            }
            else if (type == QuarterlyPrefix + "TotalRevenue")
            {
                foreach (var (end, value) in ReadPoints(points, QuarterlyPeriodType))
                {
                    quarterlyRevenue[end] = value;
                }
            }
        }

        if (annual.Count == 0)
        {
            return null;
        }

        var periods = annual
            .OrderByDescending(p => p.Key)
            .Take(MaxAnnualPeriods)
            .Select(p => new DcfAnnualPeriod(
                p.Key,
                Line(p.Value, "TotalRevenue"),
                Line(p.Value, "OperatingIncome"),
                Line(p.Value, "TaxProvision"),
                Line(p.Value, "PretaxIncome"),
                Line(p.Value, "InterestExpense"),
                Line(p.Value, "DepreciationAndAmortization"),
                Line(p.Value, "CapitalExpenditure"),
                Line(p.Value, "ChangeInWorkingCapital")))
            .ToList();

        var (ttm, latestQuarter) = TrailingTwelveMonths(quarterlyRevenue);
        return new DcfInputs(periods, ttm, latestQuarter);
    }

    // The four newest quarters count only when they really are consecutive: the first and last end ~9 months apart.
    private static (decimal? Revenue, DateOnly? LatestEnd) TrailingTwelveMonths(Dictionary<DateOnly, decimal> quarterly)
    {
        var newest = quarterly.OrderByDescending(q => q.Key).Take(QuartersInYear).ToList();
        if (newest.Count == 0)
        {
            return (null, null);
        }

        var latest = newest[0].Key;
        if (newest.Count < QuartersInYear)
        {
            return (null, latest);
        }

        var span = newest[0].Key.DayNumber - newest[^1].Key.DayNumber;
        return span is >= TtmWindowMinDays and <= TtmWindowMaxDays
            ? (newest.Sum(q => q.Value), latest)
            : (null, latest);
    }

    private static decimal? Line(Dictionary<string, decimal> period, string name)
        => period.TryGetValue(name, out var value) ? value : null;

    private static string? SeriesType(JsonElement series)
        => series.TryGetProperty("meta", out var meta) &&
           meta.TryGetProperty("type", out var types) &&
           types.ValueKind == JsonValueKind.Array &&
           types.GetArrayLength() > 0
            ? types[0].GetString()
            : null;

    private static IEnumerable<(DateOnly End, decimal Value)> ReadPoints(JsonElement points, string expectedPeriodType)
    {
        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind == JsonValueKind.Object &&
                point.TryGetProperty("asOfDate", out var asOf) &&
                DateOnly.TryParse(asOf.GetString(), CultureInfo.InvariantCulture, out var end) &&
                point.TryGetProperty("periodType", out var periodType) &&
                periodType.GetString() == expectedPeriodType &&
                point.TryGetProperty("reportedValue", out var reported) &&
                reported.TryGetProperty("raw", out var raw) &&
                raw.ValueKind == JsonValueKind.Number &&
                raw.TryGetDouble(out var value))
            {
                // Yahoo writes large amounts in scientific notation (-6.45E8); a double holds them exactly.
                yield return (end, (decimal)value);
            }
        }
    }

    public void Dispose() => cache.Dispose();
}
