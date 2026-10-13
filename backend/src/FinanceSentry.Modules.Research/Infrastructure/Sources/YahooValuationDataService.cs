namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.Extensions.Logging;

/// <summary>
/// Current valuation metrics from Yahoo's <c>quoteSummary</c> modules (feature 030, R3): trailing/
/// forward P/E and dividend yield (<c>summaryDetail</c>), enterprise value (<c>defaultKeyStatistics</c>),
/// EBITDA and consensus target (<c>financialData</c>), sector/industry (<c>assetProfile</c>), quote type
/// (<c>price</c>). Same crumb + cookie dance as the analyst/earnings clients; registered as a singleton
/// so the crumb is reused. Price and staleness are refined against the shared quote path so they match
/// the rest of the app (FR-006). Non-equity quote types return <c>NotApplicable</c>.
/// </summary>
public sealed class YahooValuationDataService(
    IHttpClientFactory httpFactory,
    IMarketDataService marketData,
    ILogger<YahooValuationDataService> logger) : IValuationDataService
{
    public const string HttpClientName = "yahoo-valuation";

    private const string CookieSeedUrl = "https://fc.yahoo.com/";
    private const string CrumbUrl = "https://query1.finance.yahoo.com/v1/test/getcrumb";
    private const string Modules = "price,summaryDetail,defaultKeyStatistics,financialData,assetProfile";
    private const int MaxPeers = 6;

    private static readonly TimeSpan CrumbTtl = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim crumbLock = new(1, 1);

    private string? crumb;
    private DateTimeOffset crumbFetchedAt;

    public async Task<ValuationCurrentMetrics?> GetCurrentMetricsAsync(string ticker, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        if (upper.Length == 0)
        {
            return null;
        }

        var client = httpFactory.CreateClient(HttpClientName);
        var crumbValue = await GetCrumbAsync(client, forceRefresh: false, ct);
        if (crumbValue is null)
        {
            logger.LogWarning("Yahoo crumb acquisition failed — cannot fetch valuation for {Ticker}", upper);
            return null;
        }

        var url = $"https://query1.finance.yahoo.com/v10/finance/quoteSummary/{Uri.EscapeDataString(upper)}"
            + $"?modules={Modules}&crumb={Uri.EscapeDataString(crumbValue)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var refreshed = await GetCrumbAsync(client, forceRefresh: true, ct);
                if (refreshed is null)
                {
                    return null;
                }

                return await GetCurrentMetricsAsync(upper, ct);
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            var parsed = Parse(json, upper);
            if (parsed is null || parsed.NotApplicable)
            {
                return parsed;
            }

            // Refine price + staleness against the shared quote path so they match the rest of the app.
            var quotes = await marketData.GetQuotesAsync([upper], ct);
            if (quotes.TryGetValue(upper, out var quote))
            {
                return parsed with
                {
                    Price = quote.Price,
                    IsStale = quote.IsStale,
                    PriceAsOf = quote.SourcePriceTime ?? quote.FetchedAt,
                };
            }

            return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Yahoo valuation fetch failed for {Ticker}", upper);
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetPeerSymbolsAsync(string ticker, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        if (upper.Length == 0)
        {
            return [];
        }

        var client = httpFactory.CreateClient(HttpClientName);
        var url = $"https://query1.finance.yahoo.com/v6/finance/recommendationsbysymbol/{Uri.EscapeDataString(upper)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            return ParsePeers(json, upper);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Yahoo peer lookup failed for {Ticker}", upper);
            return [];
        }
    }

    /// <summary>
    /// Parses a <c>quoteSummary</c> valuation response. Public + static so the contract test can assert
    /// the JSON shape against a recorded fixture. Every ratio is optional-tolerant; a missing field is
    /// <c>null</c>, never zero. Returns <c>null</c> if the result block is absent.
    /// </summary>
    public static ValuationCurrentMetrics? Parse(string json, string ticker)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("quoteSummary", out var summary) ||
            !summary.TryGetProperty("result", out var results) ||
            results.ValueKind is not JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }

        var upper = ticker.Trim().ToUpperInvariant();
        var result = results[0];

        var price = result.TryGetProperty("price", out var priceModule) ? priceModule : default;
        var summaryDetail = result.TryGetProperty("summaryDetail", out var sd) ? sd : default;
        var keyStats = result.TryGetProperty("defaultKeyStatistics", out var ks) ? ks : default;
        var financialData = result.TryGetProperty("financialData", out var fd) ? fd : default;
        var assetProfile = result.TryGetProperty("assetProfile", out var ap) ? ap : default;

        var quoteType = ReadString(price, "quoteType");
        if (!string.IsNullOrEmpty(quoteType) &&
            !string.Equals(quoteType, "EQUITY", StringComparison.OrdinalIgnoreCase))
        {
            return new ValuationCurrentMetrics(
                upper, null, null, null, null, null, null, IsStale: false, NotApplicable: true, null, null);
        }

        var trailingPe = ReadRaw(summaryDetail, "trailingPE");
        var forwardPe = ReadRaw(summaryDetail, "forwardPE") ?? ReadRaw(keyStats, "forwardPE");
        var dividendYield = ReadRaw(summaryDetail, "dividendYield");
        var enterpriseValue = ReadRaw(keyStats, "enterpriseValue");
        var ebitda = ReadRaw(financialData, "ebitda");
        var (currency, divisor) = MinorUnitCurrency.Normalize(ReadString(price, "currency"));
        var consensusTarget = ReadRaw(financialData, "targetMeanPrice") / divisor;
        var marketPrice = ReadRaw(price, "regularMarketPrice") / divisor;

        decimal? evToEbitda = enterpriseValue is { } ev && ebitda is { } eb && eb != 0m
            ? decimal.Round(ev / eb, 2)
            : null;

        return new ValuationCurrentMetrics(
            upper,
            marketPrice,
            trailingPe,
            forwardPe,
            evToEbitda,
            dividendYield,
            consensusTarget,
            IsStale: true,
            NotApplicable: false,
            ReadString(assetProfile, "sector"),
            ReadString(assetProfile, "industry"),
            ReadString(price, "longName") ?? ReadString(price, "shortName"),
            TargetLow: ReadRaw(financialData, "targetLowPrice") / divisor,
            TargetHigh: ReadRaw(financialData, "targetHighPrice") / divisor,
            AnalystCount: ReadRaw(financialData, "numberOfAnalystOpinions") is { } count ? (int)count : null,
            Currency: currency,
            MarketCap: ReadRaw(price, "marketCap") ?? ReadRaw(summaryDetail, "marketCap"));
    }

    private static IReadOnlyList<string> ParsePeers(string json, string self)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("finance", out var finance) ||
            !finance.TryGetProperty("result", out var results) ||
            results.ValueKind is not JsonValueKind.Array ||
            results.GetArrayLength() == 0 ||
            !results[0].TryGetProperty("recommendedSymbols", out var recs) ||
            recs.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var peers = new List<string>();
        foreach (var rec in recs.EnumerateArray())
        {
            var symbol = ReadString(rec, "symbol")?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(symbol) && symbol != self && !peers.Contains(symbol))
            {
                peers.Add(symbol);
            }

            if (peers.Count >= MaxPeers)
            {
                break;
            }
        }

        return peers;
    }

    // Yahoo wraps numeric fields as { "raw": <number>, "fmt": "<string>" }.
    private static decimal? ReadRaw(JsonElement parent, string property)
    {
        if (parent.ValueKind is not JsonValueKind.Object ||
            !parent.TryGetProperty(property, out var el) ||
            el.ValueKind is not JsonValueKind.Object ||
            !el.TryGetProperty("raw", out var raw) ||
            raw.ValueKind is not JsonValueKind.Number ||
            !raw.TryGetDecimal(out var value))
        {
            return null;
        }

        return value;
    }

    private static string? ReadString(JsonElement parent, string property)
        => parent.ValueKind is JsonValueKind.Object
            && parent.TryGetProperty(property, out var el)
            && el.ValueKind is JsonValueKind.String
            ? el.GetString()
            : null;

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
}
