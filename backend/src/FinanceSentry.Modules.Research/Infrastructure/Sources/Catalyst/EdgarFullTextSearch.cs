namespace FinanceSentry.Modules.Research.Infrastructure.Sources.Catalyst;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.ReferenceUniverse;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// EDGAR full-text search (<c>efts.sec.gov</c>) for 8-K filings: one query per watched item over a date range, paged, every
/// request spaced by the SEC pacer and sent on the declared-User-Agent <c>sec-edgar</c> client. The endpoint is not documented
/// by the SEC, so nothing about its shape is trusted past what is read here: a refusal, a timeout, a body that is not JSON or has
/// no <c>hits.hits</c> array all raise <see cref="CatalystFeedException"/>, and the scan falls back to the per-ticker submissions
/// feed instead of treating the silence as "no filings".
/// </summary>
public sealed partial class EdgarFullTextSearch(
    IHttpClientFactory httpFactory,
    [FromKeyedServices(CatalystPacers.Sec)] RequestPacer pacer,
    IOptions<CatalystOptions> options) : IEdgarFullTextSearch
{
    private readonly CatalystOptions _options = options.Value;

    public async Task<IReadOnlyList<CatalystFiling>> SearchAsync(
        string item, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var size = Math.Clamp(_options.FullTextPageSize, 1, 100);
        var maxPages = Math.Max(1, _options.FullTextMaxPagesPerItem);
        var found = new List<CatalystFiling>();
        for (var page = 0; page < maxPages; page++)
        {
            var (hits, total) = await ReadPageAsync(item, from, to, page * size, size, ct);
            found.AddRange(hits.SelectMany(h => h));
            if (hits.Count < size || (page + 1) * size >= total)
            {
                return found;
            }
        }

        throw new CatalystFeedException(
            $"EDGAR full-text search for item {item} holds more than the {maxPages * size} hits one run reads (Catalyst:FullTextMaxPagesPerItem); a partial read is not a complete one.");
    }

    /// <summary>The filings of every hit in one response body (a hit naming several tickers yields one filing per ticker); the hit count and the reported total.</summary>
    public static (List<List<CatalystFiling>> Hits, int Total) ParsePage(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CatalystFeedException("EDGAR full-text search answered with something that is not JSON.", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("hits", out var outer) || outer.ValueKind != JsonValueKind.Object
                || !outer.TryGetProperty("hits", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                throw new CatalystFeedException("EDGAR full-text search answered without a hits.hits array.");
            }

            var total = ReadTotal(outer);
            var hits = new List<List<CatalystFiling>>();
            foreach (var hit in array.EnumerateArray())
            {
                hits.Add(ParseHit(hit));
            }

            return (hits, Math.Max(total, hits.Count));
        }
    }

    private async Task<(List<List<CatalystFiling>> Hits, int Total)> ReadPageAsync(
        string item, DateOnly from, DateOnly to, int offset, int size, CancellationToken ct)
    {
        var query = string.Create(CultureInfo.InvariantCulture,
            $"q={Uri.EscapeDataString($"\"Item {item}\"")}&forms=8-K&dateRange=custom&startdt={from:yyyy-MM-dd}&enddt={to:yyyy-MM-dd}&from={offset}&size={size}");
        var url = $"{_options.FullTextUrl}?{query}";

        string body;
        try
        {
            await pacer.WaitAsync(ct);
            using var response = await httpFactory.CreateClient(SecEdgarService.HttpClientName).GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new CatalystFeedException(
                    $"EDGAR full-text search answered HTTP {(int)response.StatusCode} for item {item}.");
            }

            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (CatalystFeedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new CatalystFeedException($"EDGAR full-text search could not be read for item {item}: {ex.Message}", ex);
        }

        return ParsePage(body);
    }

    private static int ReadTotal(JsonElement hitsObject)
    {
        if (!hitsObject.TryGetProperty("total", out var total))
        {
            return 0;
        }

        // Elasticsearch-style: {"value": 123, "relation": "eq"}; older: a bare number.
        if (total.ValueKind == JsonValueKind.Object && total.TryGetProperty("value", out var value) && value.TryGetInt32(out var n))
        {
            return n;
        }

        return total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out var bare) ? bare : 0;
    }

    private static List<CatalystFiling> ParseHit(JsonElement hit)
    {
        if (!hit.TryGetProperty("_source", out var source) || source.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var id = hit.TryGetProperty("_id", out var idProp) ? idProp.GetString() : null;
        var accession = Text(source, "adsh") ?? id?.Split(':')[0];
        var form = Text(source, "form") ?? Text(source, "file_type");
        var filed = DateOnly.TryParse(Text(source, "file_date"), CultureInfo.InvariantCulture, out var date) ? date : (DateOnly?)null;
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(form) || filed is null)
        {
            return [];
        }

        var items = CatalystRules.ParseItems(Strings(source, "items"));
        var cik = Strings(source, "ciks").FirstOrDefault()?.TrimStart('0');
        var document = id is not null && id.Contains(':', StringComparison.Ordinal) && !string.IsNullOrEmpty(cik)
            ? $"https://www.sec.gov/Archives/edgar/data/{cik}/{accession.Replace("-", string.Empty)}/{id[(id.IndexOf(':') + 1)..]}"
            : string.Empty;

        return
        [
            .. Tickers(Strings(source, "display_names"))
                .Select(t => new CatalystFiling(t, accession, form, items, filed.Value, document, CatalystRules.SourceFullText)),
        ];
    }

    /// <summary>The tickers a display name lists: <c>"NVIDIA CORP  (NVDA)  (CIK 0001045450)"</c> gives <c>NVDA</c>; two classes give both.</summary>
    public static IReadOnlyList<string> Tickers(IEnumerable<string> displayNames)
        => [.. displayNames
            .Select(n => DisplayTickers().Match(n))
            .Where(m => m.Success)
            .SelectMany(m => m.Groups["t"].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(ReferenceListKeys.NormalizeTicker)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];

    private static string? Text(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static IEnumerable<string> Strings(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var p))
        {
            return [];
        }

        return p.ValueKind switch
        {
            JsonValueKind.String => [p.GetString() ?? string.Empty],
            JsonValueKind.Array => [.. p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty)],
            _ => [],
        };
    }

    [GeneratedRegex(@"\((?<t>[A-Za-z0-9.\-, ]+)\)\s+\(CIK\b")]
    private static partial Regex DisplayTickers();
}
