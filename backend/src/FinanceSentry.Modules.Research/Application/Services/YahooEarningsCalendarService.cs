namespace FinanceSentry.Modules.Research.Application.Services;

using System.Collections.Concurrent;
using System.Text.Json;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

// Live earnings/ex-dividend fetch from Yahoo Finance's quoteSummary "calendarEvents" module.
// Free and key-less, but the endpoint requires a cookie + crumb pair; we obtain one, cache it,
// and refresh on a 401. Fetched events are cached per-ticker for a few hours since earnings
// dates change rarely. Registered as a singleton so the crumb and cache survive across requests.
public sealed class YahooEarningsCalendarService(
    IHttpClientFactory httpFactory,
    ILogger<YahooEarningsCalendarService> logger) : IEarningsCalendarService
{
    public const string HttpClientName = "yahoo-earnings";

    private const string CookieSeedUrl = "https://fc.yahoo.com/";
    private const string CrumbUrl = "https://query1.finance.yahoo.com/v1/test/getcrumb";

    private const int MaxConcurrentFetches = 4;

    private static readonly TimeSpan CrumbTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim crumbLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CachedEvents> cache = new(StringComparer.OrdinalIgnoreCase);

    private string? crumb;
    private DateTimeOffset crumbFetchedAt;

    public async Task<IReadOnlyList<EarningsEvent>> GetForTickersAsync(
        IReadOnlyCollection<string> tickers,
        DateOnly from,
        DateOnly to,
        string? eventType,
        CancellationToken ct = default,
        bool surfaceProviderFailure = false)
    {
        var normalized = tickers
            .Select(t => t.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToArray();

        if (normalized.Length == 0)
        {
            return [];
        }

        using var throttle = new SemaphoreSlim(MaxConcurrentFetches);
        var tasks = normalized.Select(async ticker =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await FetchTickerAsync(ticker, ct);
            }
            finally
            {
                throttle.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        // Opt-in only: every existing caller leaves surfaceProviderFailure false and keeps
        // today's behaviour (an all-failed batch just contributes no events, same as this).
        if (surfaceProviderFailure && Array.TrueForAll(results, r => r.Failed))
        {
            throw new EarningsCalendarProviderException(
                $"Yahoo earnings fetch failed for all {normalized.Length} requested ticker(s).");
        }

        var typeFilter = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim().ToLowerInvariant();

        return results
            .SelectMany(r => r.Events)
            .Where(e => e.EventDate >= from && e.EventDate <= to)
            .Where(e => typeFilter is null || e.EventType == typeFilter)
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.Ticker, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<TickerFetchResult> FetchTickerAsync(string ticker, CancellationToken ct)
    {
        if (cache.TryGetValue(ticker, out var hit) && DateTimeOffset.UtcNow - hit.FetchedAt < CacheTtl)
        {
            return new TickerFetchResult(hit.Events, false);
        }

        var client = httpFactory.CreateClient(HttpClientName);

        var crumbValue = await GetCrumbAsync(client, forceRefresh: false, ct);
        if (crumbValue is null)
        {
            return new TickerFetchResult([], true);
        }

        var (events, unauthorized, failed) = await QueryYahooAsync(client, ticker, crumbValue, ct);
        if (unauthorized)
        {
            // Crumb likely stale — refresh once and retry.
            crumbValue = await GetCrumbAsync(client, forceRefresh: true, ct);
            if (crumbValue is null)
            {
                return new TickerFetchResult([], true);
            }

            (events, _, failed) = await QueryYahooAsync(client, ticker, crumbValue, ct);
        }

        if (failed)
        {
            return new TickerFetchResult([], true);
        }

        cache[ticker] = new CachedEvents(DateTimeOffset.UtcNow, events);
        return new TickerFetchResult(events, false);
    }

    private async Task<string?> GetCrumbAsync(HttpClient client, bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && crumb is not null && DateTimeOffset.UtcNow - crumbFetchedAt < CrumbTtl)
        {
            return crumb;
        }

        await crumbLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && crumb is not null && DateTimeOffset.UtcNow - crumbFetchedAt < CrumbTtl)
            {
                return crumb;
            }

            // Seed the consent/session cookies. This endpoint 404s but still sets the cookies
            // the crumb call needs — so ignore the status and any transient failure.
            try
            {
                using var seed = await client.GetAsync(CookieSeedUrl, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Yahoo cookie seed request failed (non-fatal)");
            }

            using var response = await client.GetAsync(CrumbUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Yahoo getcrumb returned {Status}", (int)response.StatusCode);
                return null;
            }

            var value = (await response.Content.ReadAsStringAsync(ct)).Trim();
            if (string.IsNullOrEmpty(value) || value.Contains('<'))
            {
                logger.LogWarning("Yahoo getcrumb returned an unexpected body");
                return null;
            }

            crumb = value;
            crumbFetchedAt = DateTimeOffset.UtcNow;
            return crumb;
        }
        finally
        {
            crumbLock.Release();
        }
    }

    private async Task<(IReadOnlyList<EarningsEvent> Events, bool Unauthorized, bool Failed)> QueryYahooAsync(
        HttpClient client, string ticker, string crumbValue, CancellationToken ct)
    {
        var url = $"https://query1.finance.yahoo.com/v10/finance/quoteSummary/{Uri.EscapeDataString(ticker)}"
            + $"?modules=calendarEvents&crumb={Uri.EscapeDataString(crumbValue)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return ([], true, false);
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            return (Parse(ticker, doc.RootElement), false, false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Yahoo earnings fetch failed for {Ticker}", ticker);
            return ([], false, true);
        }
    }

    private static IReadOnlyList<EarningsEvent> Parse(string ticker, JsonElement root)
    {
        if (!root.TryGetProperty("quoteSummary", out var summary) ||
            !summary.TryGetProperty("result", out var results) ||
            results.ValueKind is not JsonValueKind.Array ||
            results.GetArrayLength() == 0 ||
            !results[0].TryGetProperty("calendarEvents", out var calendar))
        {
            return [];
        }

        var events = new List<EarningsEvent>(3);

        if (calendar.TryGetProperty("earnings", out var earnings))
        {
            var date = FirstDate(earnings, "earningsDate");
            if (date is not null)
            {
                var isEstimate = earnings.TryGetProperty("isEarningsDateEstimate", out var est)
                    && est.ValueKind is JsonValueKind.True;
                events.Add(new EarningsEvent(ticker, EarningsEventType.Earnings, date.Value, isEstimate, "yahoo"));
            }
        }

        var exDiv = ReadDate(calendar, "exDividendDate");
        if (exDiv is not null)
        {
            events.Add(new EarningsEvent(ticker, EarningsEventType.ExDividend, exDiv.Value, false, "yahoo"));
        }

        var div = ReadDate(calendar, "dividendDate");
        if (div is not null)
        {
            events.Add(new EarningsEvent(ticker, EarningsEventType.Dividend, div.Value, false, "yahoo"));
        }

        return events;
    }

    // The earnings module carries an array of candidate dates (a range when estimated); take the earliest.
    private static DateOnly? FirstDate(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var arr) || arr.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        DateOnly? earliest = null;
        foreach (var element in arr.EnumerateArray())
        {
            var date = ReadEpoch(element);
            if (date is not null && (earliest is null || date < earliest))
            {
                earliest = date;
            }
        }

        return earliest;
    }

    // A field shaped like { "raw": 1785441600, "fmt": "2026-07-30" }.
    private static DateOnly? ReadDate(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var element) ? ReadEpoch(element) : null;

    private static DateOnly? ReadEpoch(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object ||
            !element.TryGetProperty("raw", out var raw) ||
            raw.ValueKind is not JsonValueKind.Number ||
            !raw.TryGetInt64(out var epoch))
        {
            return null;
        }

        return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime);
    }

    private readonly record struct CachedEvents(DateTimeOffset FetchedAt, IReadOnlyList<EarningsEvent> Events);

    // Failed distinguishes "this ticker's fetch errored at the provider" from "fetched fine, no
    // events" — only the former can make a surfaceProviderFailure batch throw.
    private readonly record struct TickerFetchResult(IReadOnlyList<EarningsEvent> Events, bool Failed);
}
