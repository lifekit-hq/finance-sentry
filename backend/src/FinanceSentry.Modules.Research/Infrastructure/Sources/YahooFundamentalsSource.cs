namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Yahoo Finance's fundamentals timeseries as a chain source (#837): the same key-less provider the
/// valuation snapshot reads, no crumb or cookie needed for this endpoint. It covers what EDGAR's XBRL
/// cannot — the quarterly results a foreign private issuer only furnishes on 6-K (GRAB) — but serves
/// just the last ~5 quarters and ~4 fiscal years, and its line items are Yahoo's own normalization
/// (its operating income can differ from the filed IFRS figure). Hence last in the default order:
/// the chain only reaches it for a period basis no EDGAR taxonomy supplies fresh. Results (and
/// "no data") are cached per ticker for 12 hours; a failure is not cached.
/// </summary>
public sealed class YahooFundamentalsSource(
    IHttpClientFactory httpFactory,
    TimeProvider clock,
    ILogger<YahooFundamentalsSource> logger) : IFundamentalsSource
{
    public const string HttpClientName = "yahoo-fundamentals";

    /// <summary>The <see cref="FundamentalFact.Taxonomy"/> of a Yahoo fact: Yahoo's normalized line items, not XBRL.</summary>
    public const string YahooTaxonomy = "yahoo-normalized";

    private const string QuarterlyPrefix = "quarterly";
    private const string AnnualPrefix = "annual";
    private const string QuarterlyPeriod = "3M";
    private const string AnnualPeriodType = "12M";
    private const string PerShareSuffix = "/shares";
    private const int HistoryYears = 6;

    private static readonly TimeSpan ResultTtl = TimeSpan.FromHours(12);

    // Yahoo series name -> the chain's concept. NetIncome is attributable to the parent's common
    // holders and StockholdersEquity excludes minority interest, matching the EDGAR concepts.
    private static readonly (string YahooType, string Concept)[] Concepts =
    [
        ("TotalRevenue", "Revenue"),
        ("GrossProfit", "GrossProfit"),
        ("OperatingIncome", "OperatingIncome"),
        ("NetIncomeCommonStockholders", "NetIncome"),
        ("DilutedEPS", "DilutedEPS"),
        ("StockholdersEquity", "StockholdersEquity"),
    ];

    private static readonly string TypeQuery = string.Join(
        ',', Concepts.SelectMany(c => new[] { QuarterlyPrefix + c.YahooType, AnnualPrefix + c.YahooType }));

    private readonly ConcurrentDictionary<string, Cached> cache = new(StringComparer.OrdinalIgnoreCase);

    public string Name => FundamentalsSourceNames.YahooFinance;

    public async Task<FundamentalsSourceResult> FetchAsync(string ticker, CancellationToken ct)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        var now = clock.GetUtcNow();
        if (cache.TryGetValue(upper, out var hit) && now - hit.FetchedAt < ResultTtl)
        {
            return hit.Result;
        }

        var documentUrl = DocumentUrl(upper);
        var url = $"{documentUrl}&period1={now.AddYears(-HistoryYears).ToUnixTimeSeconds()}&period2={now.ToUnixTimeSeconds()}";

        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);

            var facts = Parse(json, upper, documentUrl, now);
            var result = facts.Count > 0
                ? FundamentalsSourceResult.WithFacts(facts)
                : FundamentalsSourceResult.NoData("no fundamentals timeseries");
            cache[upper] = new Cached(now, result);
            return result;
        }
        // An HttpClient timeout is a TaskCanceledException too: only the caller's own cancellation propagates.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Yahoo fundamentals fetch failed for {Ticker}", upper);
            return FundamentalsSourceResult.Failed($"Yahoo fundamentals fetch failed: {ex.Message}");
        }
    }

    /// <summary>The endpoint a ticker's facts are read from, without the time window: the provenance document.</summary>
    public static string DocumentUrl(string ticker)
    {
        var symbol = Uri.EscapeDataString(ticker);
        return $"https://query1.finance.yahoo.com/ws/fundamentals-timeseries/v1/finance/timeseries/{symbol}"
            + $"?symbol={symbol}&type={TypeQuery}";
    }

    /// <summary>
    /// Parses a timeseries response into facts. Public + static so the contract test can assert the
    /// JSON shape against a recorded fixture. A 12-month value is a fiscal year (<c>FY</c>); a 3-month
    /// value keeps Yahoo's <c>3M</c> period label, since Yahoo does not say which fiscal quarter it is.
    /// Null or malformed points are skipped, never zeroed.
    /// </summary>
    public static IReadOnlyList<FundamentalFact> Parse(
        string json, string ticker, string documentUrl, DateTimeOffset ingestedAt)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("timeseries", out var timeseries) ||
            !timeseries.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var provenance = new SourceProvenance(FundamentalsSourceNames.YahooFinance, documentUrl, ingestedAt);
        var facts = new List<FundamentalFact>();
        foreach (var series in results.EnumerateArray())
        {
            var type = SeriesType(series);
            if (type is null || !series.TryGetProperty(type, out var points) || points.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var annual = type.StartsWith(AnnualPrefix, StringComparison.Ordinal);
            var yahooType = type[(annual ? AnnualPrefix : QuarterlyPrefix).Length..];
            var concept = Concepts.FirstOrDefault(c => c.YahooType == yahooType).Concept;
            if (concept is null)
            {
                continue;
            }

            foreach (var point in points.EnumerateArray())
            {
                var fact = ParsePoint(point, ticker, concept, yahooType, annual, provenance);
                if (fact is not null)
                {
                    facts.Add(fact);
                }
            }
        }

        return facts
            .OrderBy(f => f.Concept, StringComparer.Ordinal)
            .ThenByDescending(f => f.PeriodEnd)
            .ToList();
    }

    private static string? SeriesType(JsonElement series)
        => series.TryGetProperty("meta", out var meta) &&
           meta.TryGetProperty("type", out var types) &&
           types.ValueKind == JsonValueKind.Array &&
           types.GetArrayLength() > 0
            ? types[0].GetString()
            : null;

    private static FundamentalFact? ParsePoint(
        JsonElement point, string ticker, string concept, string yahooType, bool annual, SourceProvenance provenance)
    {
        if (point.ValueKind != JsonValueKind.Object ||
            !point.TryGetProperty("asOfDate", out var asOf) ||
            !DateOnly.TryParse(asOf.GetString(), CultureInfo.InvariantCulture, out var periodEnd) ||
            !point.TryGetProperty("reportedValue", out var reported) ||
            !reported.TryGetProperty("raw", out var raw) ||
            raw.ValueKind != JsonValueKind.Number ||
            !raw.TryGetDouble(out var value))
        {
            return null;
        }

        var periodType = point.TryGetProperty("periodType", out var pt) ? pt.GetString() : null;
        if (annual != (periodType == AnnualPeriodType))
        {
            return null; // a series whose points disagree with its own name — not trusted.
        }

        var currency = point.TryGetProperty("currencyCode", out var cc) && !string.IsNullOrEmpty(cc.GetString())
            ? cc.GetString()!
            : "USD";
        var unit = concept == "DilutedEPS" ? currency + PerShareSuffix : currency;

        return new FundamentalFact(
            ticker,
            concept,
            yahooType,
            unit,
            (decimal)value,
            periodEnd,
            annual ? "FY" : QuarterlyPeriod,
            FiscalYear: null,
            Form: string.Empty,
            Taxonomy: YahooTaxonomy,
            SourceProvenance: provenance);
    }

    private readonly record struct Cached(DateTimeOffset FetchedAt, FundamentalsSourceResult Result);
}
