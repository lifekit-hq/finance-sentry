namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using System.Net;
using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services.Etf;
using Microsoft.Extensions.Logging;

/// <summary>
/// An ETF's quote type and largest holdings from Yahoo's <c>quoteSummary</c> <c>price</c> and <c>topHoldings</c>
/// modules (issue #1324). Same crumb and cookie dance as the valuation client, on its named HTTP client (and so its
/// cookie container). Yahoo lists the top 10 with each weight as a fraction of the fund; they come back here as
/// percent. A symbol whose quote type is not <c>ETF</c> answers <see cref="FundKind.NotAFund"/>; a failed read
/// answers <see cref="FundKind.Unknown"/>.
/// </summary>
public sealed class YahooFundHoldingsProvider(
    IHttpClientFactory httpFactory,
    TimeProvider clock,
    ILogger<YahooFundHoldingsProvider> logger) : IFundHoldingsProvider
{
    public const string HttpClientName = YahooValuationDataService.HttpClientName;

    private const string CookieSeedUrl = "https://fc.yahoo.com/";
    private const string CrumbUrl = "https://query1.finance.yahoo.com/v1/test/getcrumb";
    private const string Modules = "price,topHoldings";
    private const string EtfQuoteType = "ETF";
    private const decimal PercentScale = 100m;
    private const int WeightDecimals = 2;

    private static readonly TimeSpan CrumbTtl = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim crumbLock = new(1, 1);

    private string? crumb;
    private DateTimeOffset crumbFetchedAt;

    public async Task<FundHoldingsLookup> LookupAsync(string symbol, CancellationToken ct)
    {
        var upper = symbol.Trim().ToUpperInvariant();
        if (upper.Length == 0)
        {
            return FundHoldingsLookup.Unknown;
        }

        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            var crumbValue = await GetCrumbAsync(client, forceRefresh: false, ct);
            if (crumbValue is null)
            {
                logger.LogWarning("Yahoo crumb acquisition failed - cannot read holdings for {Symbol}", upper);
                return FundHoldingsLookup.Unknown;
            }

            var json = await FetchAsync(client, upper, crumbValue, ct);
            if (json is null)
            {
                var refreshed = await GetCrumbAsync(client, forceRefresh: true, ct);
                json = refreshed is null ? null : await FetchAsync(client, upper, refreshed, ct);
            }

            return json is null ? FundHoldingsLookup.Unknown : Parse(json, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Yahoo holdings read failed for {Symbol}", upper);
            return FundHoldingsLookup.Unknown;
        }
    }

    /// <summary>
    /// Parses a <c>quoteSummary</c> response for <c>price,topHoldings</c>. Public and static so a recorded fixture can
    /// pin the JSON shape. An absent result block is <see cref="FundKind.Unknown"/>; a quote type other than ETF is
    /// <see cref="FundKind.NotAFund"/>; an ETF with no usable holdings is a <see cref="FundKind.Fund"/> with none.
    /// Lines without a positive weight are dropped, the rest ordered by weight and cut to <see cref="FundHoldingsLookup.TopCount"/>.
    /// </summary>
    public static FundHoldingsLookup Parse(string json, DateTimeOffset readAt)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("quoteSummary", out var summary)
            || !summary.TryGetProperty("result", out var results)
            || results.ValueKind is not JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            return FundHoldingsLookup.Unknown;
        }

        var result = results[0];
        var quoteType = result.TryGetProperty("price", out var price) ? ReadString(price, "quoteType") : null;
        if (!string.Equals(quoteType, EtfQuoteType, StringComparison.OrdinalIgnoreCase))
        {
            return FundHoldingsLookup.NotAFund;
        }

        var holdings = new List<FundHolding>();
        if (result.TryGetProperty("topHoldings", out var top)
            && top.ValueKind is JsonValueKind.Object
            && top.TryGetProperty("holdings", out var list)
            && list.ValueKind is JsonValueKind.Array)
        {
            foreach (var line in list.EnumerateArray())
            {
                if (ReadRaw(line, "holdingPercent") is not { } weight || weight <= 0m)
                {
                    continue;
                }

                holdings.Add(new FundHolding(
                    NullIfBlank(ReadString(line, "symbol"))?.ToUpperInvariant(),
                    NullIfBlank(ReadString(line, "holdingName")),
                    decimal.Round(weight * PercentScale, WeightDecimals)));
            }
        }

        var top10 = holdings.OrderByDescending(h => h.WeightPct).Take(FundHoldingsLookup.TopCount).ToList();
        return new FundHoldingsLookup(
            FundKind.Fund, top10, top10.Count == 0 ? null : FundHoldingsSources.Yahoo, top10.Count == 0 ? null : readAt);
    }

    private static async Task<string?> FetchAsync(HttpClient client, string symbol, string crumbValue, CancellationToken ct)
    {
        var url = $"https://query1.finance.yahoo.com/v10/finance/quoteSummary/{Uri.EscapeDataString(symbol)}"
            + $"?modules={Modules}&crumb={Uri.EscapeDataString(crumbValue)}";
        using var response = await client.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    // Yahoo wraps numeric fields as { "raw": <number>, "fmt": "<string>" }.
    private static decimal? ReadRaw(JsonElement parent, string property) =>
        parent.ValueKind is JsonValueKind.Object
        && parent.TryGetProperty(property, out var el)
        && el.ValueKind is JsonValueKind.Object
        && el.TryGetProperty("raw", out var raw)
        && raw.ValueKind is JsonValueKind.Number
        && raw.TryGetDecimal(out var value)
            ? value
            : null;

    private static string? ReadString(JsonElement parent, string property) =>
        parent.ValueKind is JsonValueKind.Object
        && parent.TryGetProperty(property, out var el)
        && el.ValueKind is JsonValueKind.String
            ? el.GetString()
            : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<string?> GetCrumbAsync(HttpClient client, bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && crumb is not null && clock.GetUtcNow() - crumbFetchedAt < CrumbTtl)
        {
            return crumb;
        }

        await crumbLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && crumb is not null && clock.GetUtcNow() - crumbFetchedAt < CrumbTtl)
            {
                return crumb;
            }

            try
            {
                using var seed = await client.GetAsync(CookieSeedUrl, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
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
            crumbFetchedAt = clock.GetUtcNow();
            return crumb;
        }
        finally
        {
            crumbLock.Release();
        }
    }
}
