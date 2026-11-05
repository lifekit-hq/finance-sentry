namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Caching;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="IPriceBarService"/> over Yahoo's free chart endpoint (the one the quote and dossier-chart paths
/// already read): full open/high/low/close/volume bars at the interval asked. Results are cached per ticker,
/// interval and range - five minutes for intraday bars, which keep forming, thirty for the rest; a failure is
/// not cached. Singleton so the cache survives across requests.
/// </summary>
public sealed class YahooPriceBarService(
    IHttpClientFactory httpFactory,
    TimeProvider clock,
    ILogger<YahooPriceBarService> logger) : IPriceBarService, IDisposable
{
    private const int MaxCachedSeries = 256;
    private static readonly TimeSpan IntradayTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SettledTtl = TimeSpan.FromMinutes(30);

    private readonly TtlCache<PriceBarSeries> intradayCache = new(clock, IntradayTtl, MaxCachedSeries);
    private readonly TtlCache<PriceBarSeries> settledCache = new(clock, SettledTtl, MaxCachedSeries);

    public Task<PriceBarSeries> GetAsync(
        string yahooTicker, string interval, string sourceRange, CancellationToken ct = default)
    {
        var ticker = YahooMarketDataService.ResolveYahooTicker(yahooTicker.Trim().ToUpperInvariant());
        if (ticker.Length == 0)
        {
            return Task.FromResult(PriceBarSeries.Empty);
        }

        var cache = PriceInterval.IsIntraday(interval) ? intradayCache : settledCache;
        return cache.GetOrFetchAsync(
            $"{ticker}|{interval}|{sourceRange}", token => FetchAsync(ticker, interval, sourceRange, token), ct);
    }

    public void Dispose()
    {
        intradayCache.Dispose();
        settledCache.Dispose();
    }

    /// <summary>Parses a chart response; public + static so a test can assert it against a fixture.</summary>
    public static PriceBarSeries Parse(JsonElement root)
    {
        if (!root.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind is not JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return PriceBarSeries.Empty;
        }

        var result = results[0];
        string? quoted = null;
        string? timeZone = null;
        long offset = 0;
        if (result.TryGetProperty("meta", out var meta))
        {
            quoted = ReadString(meta, "currency");
            timeZone = ReadString(meta, "exchangeTimezoneName");
            offset = meta.TryGetProperty("gmtoffset", out var gmt) && gmt.TryGetInt64(out var seconds) ? seconds : 0;
        }

        var (currency, divisor) = MinorUnitCurrency.Normalize(quoted);

        if (!result.TryGetProperty("timestamp", out var timestamps) ||
            timestamps.ValueKind is not JsonValueKind.Array ||
            !result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quotes) ||
            quotes.ValueKind is not JsonValueKind.Array ||
            quotes.GetArrayLength() == 0)
        {
            return new PriceBarSeries(currency, timeZone, []);
        }

        var quote = quotes[0];
        var bars = new List<PriceBar>();
        var count = timestamps.GetArrayLength();
        for (var i = 0; i < count; i++)
        {
            // A row the source reports without all four prices is skipped: no bar is made up for it.
            if (!timestamps[i].TryGetInt64(out var epoch) ||
                Read(quote, "open", i) is not decimal open ||
                Read(quote, "high", i) is not decimal high ||
                Read(quote, "low", i) is not decimal low ||
                Read(quote, "close", i) is not decimal close ||
                high < low)
            {
                continue;
            }

            var volume = Read(quote, "volume", i) is decimal traded ? (long)traded : 0L;
            bars.Add(new PriceBar(epoch + offset, open / divisor, high / divisor, low / divisor, close / divisor, volume));
        }

        // A weekly or monthly series can carry the live day as a second row; keep one bar per start time.
        var ordered = bars
            .GroupBy(b => b.Time)
            .Select(g => g.Last())
            .OrderBy(b => b.Time)
            .ToList();
        return new PriceBarSeries(currency, timeZone, ordered);
    }

    private static decimal? Read(JsonElement quote, string name, int index)
    {
        if (!quote.TryGetProperty(name, out var values) ||
            values.ValueKind is not JsonValueKind.Array ||
            index >= values.GetArrayLength())
        {
            return null;
        }

        var element = values[index];
        return element.ValueKind is JsonValueKind.Number && element.TryGetDecimal(out var value) ? value : null;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private async Task<(PriceBarSeries Value, bool Cacheable)> FetchAsync(
        string ticker, string interval, string sourceRange, CancellationToken ct)
    {
        var client = httpFactory.CreateClient(YahooMarketDataService.HttpClientName);
        var url = $"/v8/finance/chart/{Uri.EscapeDataString(ticker)}?interval={PriceInterval.ToSource(interval)}&range={sourceRange}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (PriceBarSeries.Empty, true);
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return (Parse(doc.RootElement), true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Yahoo price bars fetch failed for {Ticker} ({Interval}, {Range})", ticker, interval, sourceRange);
            return (PriceBarSeries.Empty, false);
        }
    }
}
