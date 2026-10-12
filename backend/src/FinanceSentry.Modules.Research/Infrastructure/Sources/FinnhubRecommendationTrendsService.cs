namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Globalization;
using System.Net;
using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Finnhub <c>/stock/recommendation</c> — monthly aggregate analyst consensus per ticker
/// (feature 037). The module's first documented structured API integration; contract at
/// <c>specs/037-structured-data-sources/contracts/finnhub-recommendation.md</c>. Keyed via
/// <c>X-Finnhub-Token</c> header (configured on the named client — never the token query param);
/// no key ⇒ <see cref="IsConfigured"/> is false and callers skip quietly (FR-002).
/// </summary>
public sealed class FinnhubRecommendationTrendsService(
    IHttpClientFactory httpFactory,
    IOptions<AnalystSourcesOptions> options,
    ILogger<FinnhubRecommendationTrendsService> logger) : IRecommendationTrendsService
{
    public const string HttpClientName = "finnhub";

    public const string SourceName = "finnhub";

    /// <summary>Restated months per ticker worth persisting; bounds writes on long histories.</summary>
    private const int MaxMonthsPerTicker = 12;

    private static readonly string[] CountFields = ["strongBuy", "buy", "hold", "sell", "strongSell"];

    public bool IsConfigured =>
        options.Value.Finnhub.Enabled && !string.IsNullOrWhiteSpace(options.Value.Finnhub.ApiKey);

    public async Task<IReadOnlyList<RecommendationTrend>> FetchAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default)
    {
        var normalized = tickers
            .Select(t => t.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToArray();

        if (!IsConfigured || normalized.Length == 0)
        {
            return [];
        }

        var client = httpFactory.CreateClient(HttpClientName);
        var interval = TimeSpan.FromMinutes(1.0 / Math.Max(1, options.Value.Finnhub.RequestsPerMinute));

        var all = new List<RecommendationTrend>();
        var failures = 0;
        var first = true;
        foreach (var ticker in normalized)
        {
            if (!first)
            {
                await Task.Delay(interval, ct);
            }

            first = false;
            var rows = await FetchTickerAsync(client, ticker, ct);
            if (rows is null)
            {
                failures++;
                continue;
            }

            all.AddRange(rows);
        }

        if (failures == normalized.Length)
        {
            throw new AnalystSourceParseException(
                $"Finnhub recommendation fetch failed for all {normalized.Length} tickers — provider broken or unreachable.");
        }

        logger.LogInformation(
            "Finnhub recommendation trends fetched {Count} months across {Tickers} tickers",
            all.Count, normalized.Length);
        return all;
    }

    // A 429 (or 5xx) is retried by the named client's resilience handler, which honours Retry-After; what
    // reaches this method is the final answer. Returns null on a swallowed per-ticker failure (counted toward the all-failed check).
    // Auth failures throw immediately: they are global, and retrying other tickers only hammers
    // the provider with a bad key (or a paywalled endpoint) — the health/alert path must fire.
    private async Task<IReadOnlyList<RecommendationTrend>?> FetchTickerAsync(
        HttpClient client, string ticker, CancellationToken ct)
    {
        var url = $"stock/recommendation?symbol={Uri.EscapeDataString(ticker)}";

        try
        {
            using var response = await client.GetAsync(url, ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new AnalystSourceParseException(
                    $"Finnhub returned {(int)response.StatusCode} — invalid API key or endpoint moved behind a paywall.");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Finnhub recommendation returned {Status} for {Ticker} — skipping",
                    (int)response.StatusCode, ticker);
                return null;
            }

            return StampAndTrim(Parse(await response.Content.ReadAsStringAsync(ct), ticker));
        }
        catch (AnalystSourceParseException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Finnhub recommendation fetch failed for {Ticker} — skipping", ticker);
            return null;
        }
    }

    private static IReadOnlyList<RecommendationTrend> StampAndTrim(IReadOnlyList<RecommendationTrend> rows)
    {
        var now = DateTimeOffset.UtcNow;
        var trimmed = rows
            .OrderByDescending(r => r.Period)
            .Take(MaxMonthsPerTicker)
            .ToList();
        foreach (var row in trimmed)
        {
            row.Source = SourceName;
            row.IngestedAt = now;
        }

        return trimmed;
    }

    /// <summary>
    /// Parses the <c>/stock/recommendation</c> array for one ticker. Public + static so the
    /// contract test asserts the JSON shape against the documented sample (Yahoo precedent).
    /// Tolerant per row (missing counts → 0, malformed period → skip, all-zero months → skip);
    /// loud on a structurally-broken body (non-array root, HTML challenge).
    /// </summary>
    public static IReadOnlyList<RecommendationTrend> Parse(string json, string ticker)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AnalystSourceParseException(
                $"Finnhub recommendation body is not JSON ({ex.Message}) — markup drift or challenge page.");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind is not JsonValueKind.Array)
            {
                throw new AnalystSourceParseException(
                    "Finnhub recommendation body is not the documented JSON array — contract drift.");
            }

            var upper = ticker.Trim().ToUpperInvariant();
            var trends = new List<RecommendationTrend>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind is not JsonValueKind.Object)
                {
                    continue;
                }

                var period = ReadPeriod(item);
                if (period is null)
                {
                    continue;
                }

                var counts = CountFields.Select(f => ReadCount(item, f)).ToArray();
                if (counts.All(c => c == 0))
                {
                    continue;
                }

                trends.Add(new RecommendationTrend
                {
                    Ticker = upper,
                    Period = period.Value,
                    StrongBuy = counts[0],
                    Buy = counts[1],
                    Hold = counts[2],
                    Sell = counts[3],
                    StrongSell = counts[4],
                });
            }

            return trends;
        }
    }

    private static DateOnly? ReadPeriod(JsonElement item)
    {
        if (!item.TryGetProperty("period", out var el) || el.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        return DateOnly.TryParse(el.GetString(), CultureInfo.InvariantCulture, out var period)
            ? period
            : null;
    }

    private static int ReadCount(JsonElement item, string property)
        => item.TryGetProperty(property, out var el) && el.ValueKind is JsonValueKind.Number && el.TryGetInt32(out var value)
            ? value
            : 0;
}
