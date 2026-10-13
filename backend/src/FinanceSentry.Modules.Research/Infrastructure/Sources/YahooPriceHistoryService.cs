namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Infrastructure.Caching;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IPriceHistoryService"/> over Yahoo's free chart endpoint (the one the quote path already
/// reads): daily bars up to a year, weekly for five years, monthly for the whole history, so no range
/// returns more than a few hundred points. Results are cached per ticker and range for 30 minutes; a
/// failure is not cached. Singleton so the cache survives across requests.
/// </summary>
public sealed class YahooPriceHistoryService(
    IHttpClientFactory httpFactory,
    TimeProvider clock,
    ILogger<YahooPriceHistoryService> logger) : IPriceHistoryService, IDisposable
{
    private const int MaxCachedSeries = 256;
    private const decimal MinorUnitsPerMajor = 100m;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private readonly TtlCache<PriceHistory> cache = new(clock, CacheTtl, MaxCachedSeries);

    public Task<PriceHistory> GetAsync(string yahooTicker, string range, CancellationToken ct = default)
    {
        var ticker = YahooMarketDataService.ResolveYahooTicker(yahooTicker.Trim().ToUpperInvariant());
        var canonical = ChartRange.Normalize(range) ?? ChartRange.OneYear;
        if (ticker.Length == 0)
        {
            return Task.FromResult(PriceHistory.Empty);
        }

        return cache.GetOrFetchAsync($"{ticker}|{canonical}", token => FetchAsync(ticker, canonical, token), ct);
    }

    public void Dispose() => cache.Dispose();

    private async Task<(PriceHistory Value, bool Cacheable)> FetchAsync(string ticker, string range, CancellationToken ct)
    {
        var (yahooRange, interval) = range switch
        {
            ChartRange.OneMonth => ("1mo", "1d"),
            ChartRange.SixMonths => ("6mo", "1d"),
            ChartRange.YearToDate => ("ytd", "1d"),
            ChartRange.FiveYears => ("5y", "1wk"),
            ChartRange.Max => ("max", "1mo"),
            _ => ("1y", "1d"),
        };

        var client = httpFactory.CreateClient(YahooMarketDataService.HttpClientName);
        var url = $"/v8/finance/chart/{Uri.EscapeDataString(ticker)}?interval={interval}&range={yahooRange}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (PriceHistory.Empty, true);
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return (Parse(doc.RootElement), true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Yahoo price history fetch failed for {Ticker} ({Range})", ticker, range);
            return (PriceHistory.Empty, false);
        }
    }

    /// <summary>Parses a chart response; public + static so a test can assert it against a fixture.</summary>
    public static PriceHistory Parse(JsonElement root)
    {
        if (!root.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind is not JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return PriceHistory.Empty;
        }

        var result = results[0];
        var quoted = result.TryGetProperty("meta", out var meta)
            && meta.TryGetProperty("currency", out var c)
            && c.ValueKind is JsonValueKind.String
                ? c.GetString()?.Trim()
                : null;
        var (currency, divisor) = NormalizeCurrency(quoted);

        // A weekly or monthly series can carry the live day as a second bar in its last period; keep one per date.
        var closes = YahooMarketDataService.ReadDailyBars(result)
            .GroupBy(b => b.Date)
            .Select(g => g.Last())
            .OrderBy(b => b.Date)
            .Select(b => divisor == 1m ? b : b with { Close = b.Close / divisor })
            .ToList();

        return new PriceHistory(currency, closes);
    }

    /// <summary>
    /// Yahoo quotes some exchanges in a minor unit (LSE pence "GBp"/"GBX", Johannesburg cents "ZAc", Tel Aviv
    /// agorot "ILA"); the series is returned in the major currency, with the divisor that converts the quoted values.
    /// </summary>
    private static (string? Currency, decimal Divisor) NormalizeCurrency(string? quoted)
        => quoted switch
        {
            null or "" => (null, 1m),
            "GBp" or "GBX" => ("GBP", MinorUnitsPerMajor),
            "ZAc" => ("ZAR", MinorUnitsPerMajor),
            "ILA" => ("ILS", MinorUnitsPerMajor),
            _ => (quoted.ToUpperInvariant(), 1m),
        };
}
