namespace FinanceSentry.Modules.Research.Application.Services;

using System.Text.Json;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

public class YahooMarketDataService(
    IHttpClientFactory httpFactory,
    IQuoteCacheRepository cache,
    ILogger<YahooMarketDataService> logger) : IMarketDataService
{
    public const string HttpClientName = "yahoo-finance";

    private const int MaxConcurrentFetches = 6;
    private const string RegularSession = "regular";
    private const string PreMarketSession = "pre_market";
    private const string PostMarketSession = "post_market";
    private const string ClosedSession = "closed";
    private const string UnknownSession = "unknown";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlyDictionary<string, string> YahooTickerAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MHPC"] = "MHPC.IL",
        };

    public async Task<IReadOnlyDictionary<string, QuoteCacheEntry>> GetQuotesAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
    {
        if (tickers.Count == 0)
        {
            return new Dictionary<string, QuoteCacheEntry>();
        }

        var normalized = tickers
            .Select(t => t.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToArray();

        var cached = await cache.GetFreshAsync(normalized, CacheTtl, ct);
        var missing = normalized.Where(t => !cached.ContainsKey(t)).ToArray();

        if (missing.Length == 0)
        {
            return cached;
        }

        var fetched = await FetchManyAsync(missing, ct);

        if (fetched.Count > 0)
        {
            await cache.UpsertManyAsync(fetched.Values.ToArray(), ct);
        }

        var result = new Dictionary<string, QuoteCacheEntry>(cached);
        foreach (var (ticker, entry) in fetched)
        {
            result[ticker] = entry;
        }

        return result;
    }

    private async Task<Dictionary<string, QuoteCacheEntry>> FetchManyAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct)
    {
        var result = new Dictionary<string, QuoteCacheEntry>();
        using var throttle = new SemaphoreSlim(MaxConcurrentFetches);

        var tasks = tickers.Select(async ticker =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await FetchOneAsync(ticker, ct);
            }
            finally
            {
                throttle.Release();
            }
        });

        foreach (var entry in await Task.WhenAll(tasks))
        {
            if (entry is not null)
            {
                result[entry.Ticker] = entry;
            }
        }

        return result;
    }

    private async Task<QuoteCacheEntry?> FetchOneAsync(string ticker, CancellationToken ct)
    {
        var client = httpFactory.CreateClient(HttpClientName);
        var yahooTicker = ResolveYahooTicker(ticker);
        var url = $"/v8/finance/chart/{Uri.EscapeDataString(yahooTicker)}?interval=1d&range=5d";

        try
        {
            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
                !chart.TryGetProperty("result", out var resultArray) ||
                resultArray.ValueKind is not JsonValueKind.Array ||
                resultArray.GetArrayLength() == 0)
            {
                return null;
            }

            var first = resultArray[0];
            if (!first.TryGetProperty("meta", out var meta))
            {
                return null;
            }

            var symbol = meta.TryGetProperty("symbol", out var symbolProp)
                ? symbolProp.GetString() ?? ticker
                : ticker;
            if (!IsExpectedYahooSymbol(ticker, yahooTicker, symbol))
            {
                return null;
            }

            var marketState = meta.TryGetProperty("marketState", out var marketStateProp)
                ? marketStateProp.GetString() ?? "unknown"
                : "unknown";
            var regularMarketPrice = ReadDecimal(meta, "regularMarketPrice");
            var preMarketPrice = ReadDecimal(meta, "preMarketPrice");
            var postMarketPrice = ReadDecimal(meta, "postMarketPrice");
            var selected = SelectSessionPrice(marketState, regularMarketPrice, preMarketPrice, postMarketPrice);
            if (selected.Price is null)
            {
                return null;
            }

            var currency = meta.TryGetProperty("currency", out var currencyProp)
                ? currencyProp.GetString() ?? "USD"
                : "USD";
            var regularMarketTime = ReadUnixTime(meta, "regularMarketTime");
            var prev = PriorSessionClose(first, regularMarketTime) ?? ReadDecimal(meta, "previousClose");
            var sourcePriceTime = selected.Session switch
            {
                PreMarketSession => ReadUnixTime(meta, "preMarketTime") ?? regularMarketTime,
                PostMarketSession => ReadUnixTime(meta, "postMarketTime") ?? regularMarketTime,
                _ => regularMarketTime,
            };

            return new QuoteCacheEntry
            {
                Ticker = ticker.ToUpperInvariant(),
                ResolvedTicker = symbol.ToUpperInvariant(),
                Price = selected.Price.Value,
                PreviousClose = prev,
                Currency = currency,
                FetchedAt = DateTimeOffset.UtcNow,
                MarketState = marketState,
                Session = selected.Session,
                IsStale = selected.IsStale,
                SourcePriceTime = sourcePriceTime,
                RegularMarketTime = regularMarketTime,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Yahoo Finance quote fetch failed for {Ticker}", ticker);
            return null;
        }
    }

    public async Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string ticker, DateOnly since, CancellationToken ct = default)
    {
        var normalized = ticker.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return [];
        }

        var range = ResolveRange(since);
        var client = httpFactory.CreateClient(HttpClientName);
        var url = $"/v8/finance/chart/{Uri.EscapeDataString(normalized)}?interval=1d&range={range}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
                !chart.TryGetProperty("result", out var resultArray) ||
                resultArray.ValueKind is not JsonValueKind.Array ||
                resultArray.GetArrayLength() == 0)
            {
                return [];
            }

            return ReadDailyBars(resultArray[0]).Where(c => c.Date >= since).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Yahoo Finance daily close fetch failed for {Ticker}", normalized);
            return [];
        }
    }

    /// <summary>
    /// The previous session's close, read from the chart's daily bars: the last close dated before the
    /// session <paramref name="regularMarketTime"/> belongs to (or before the latest bar when that time is
    /// absent). <c>chartPreviousClose</c> is not used — on a 5-day range it is the close before the whole
    /// window, which would turn a daily change into a weekly one.
    /// </summary>
    private static decimal? PriorSessionClose(JsonElement result, DateTimeOffset? regularMarketTime)
    {
        var bars = ReadDailyBars(result);
        if (bars.Count == 0)
        {
            return null;
        }

        var sessionDate = regularMarketTime is { } time
            ? DateOnly.FromDateTime(time.UtcDateTime)
            : bars[^1].Date;

        return bars.LastOrDefault(b => b.Date < sessionDate)?.Close;
    }

    private static List<DailyClose> ReadDailyBars(JsonElement result)
    {
        var bars = new List<DailyClose>();
        if (!result.TryGetProperty("timestamp", out var timestamps) ||
            timestamps.ValueKind is not JsonValueKind.Array ||
            !result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quoteArray) ||
            quoteArray.ValueKind is not JsonValueKind.Array ||
            quoteArray.GetArrayLength() == 0 ||
            !quoteArray[0].TryGetProperty("close", out var closes) ||
            closes.ValueKind is not JsonValueKind.Array)
        {
            return bars;
        }

        var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
        for (var i = 0; i < count; i++)
        {
            var closeElement = closes[i];
            if (closeElement.ValueKind is not JsonValueKind.Number ||
                !closeElement.TryGetDecimal(out var close) ||
                !timestamps[i].TryGetInt64(out var epochSeconds))
            {
                continue;
            }

            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime);
            bars.Add(new DailyClose(date, close));
        }

        return bars;
    }

    private static string ResolveRange(DateOnly since)
    {
        var daysAgo = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - since.DayNumber;

        return daysAgo switch
        {
            <= 30 => "1mo",
            <= 90 => "3mo",
            <= 180 => "6mo",
            <= 365 => "1y",
            <= 730 => "2y",
            <= 1825 => "5y",
            <= 3650 => "10y",
            _ => "max",
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.TryGetDecimal(out var value) ? value : null,
            _ => null,
        };
    }

    private static string ResolveYahooTicker(string ticker)
    {
        return YahooTickerAliases.TryGetValue(ticker, out var alias)
            ? alias
            : ticker;
    }

    private static bool IsExpectedYahooSymbol(string requestedTicker, string yahooTicker, string resolvedSymbol)
    {
        return string.Equals(resolvedSymbol, yahooTicker, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resolvedSymbol, requestedTicker, StringComparison.OrdinalIgnoreCase);
    }

    private static (decimal? Price, string Session, bool IsStale) SelectSessionPrice(
        string marketState,
        decimal? regularMarketPrice,
        decimal? preMarketPrice,
        decimal? postMarketPrice)
    {
        return marketState.ToUpperInvariant() switch
        {
            "PRE" or "PREPRE" when preMarketPrice is not null => (preMarketPrice, PreMarketSession, false),
            "POST" or "POSTPOST" when postMarketPrice is not null => (postMarketPrice, PostMarketSession, false),
            "REGULAR" when regularMarketPrice is not null => (regularMarketPrice, RegularSession, false),
            "CLOSED" when regularMarketPrice is not null => (regularMarketPrice, ClosedSession, true),
            _ when regularMarketPrice is not null => (regularMarketPrice, UnknownSession, true),
            _ => (null, UnknownSession, true),
        };
    }

    private static DateTimeOffset? ReadUnixTime(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop) ||
            prop.ValueKind is not JsonValueKind.Number ||
            !prop.TryGetInt64(out var value))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(value);
    }
}
