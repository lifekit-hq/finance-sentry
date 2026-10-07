namespace FinanceSentry.Modules.Research.Application.Services;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Ports;
using Microsoft.Extensions.Logging;

// Live SEC EDGAR access (data.sec.gov) — filings + XBRL fundamentals. Free and key-less, but SEC
// policy requires a descriptive User-Agent (configured on the named client) and a 10 req/s ceiling,
// which the concurrency cap + caches stay well under. Registered as a singleton so the ticker->CIK
// map and per-ticker results are cached across requests. Nothing is persisted. Fundamentals reach
// readers through the provider chain (SecEdgarFundamentalsSource, one per taxonomy), never directly.
public sealed class SecEdgarService(
    IHttpClientFactory httpFactory,
    ILogger<SecEdgarService> logger) : ISecEdgarService
{
    public const string HttpClientName = "sec-edgar";

    private const string TickerMapUrl = "https://www.sec.gov/files/company_tickers.json";

    private static readonly string[] DefaultFormTypes = ["10-K", "10-Q", "8-K"];

    // Registered-fund filings: shareholder reports, portfolio holdings, prospectus updates, census.
    private static readonly string[] FundFormPrefixes = ["N-CSR", "N-PORT", "NPORT", "N-CEN", "485BPOS", "497", "N-1A", "N-2"];

    private static readonly TimeSpan TickerMapTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResultTtl = TimeSpan.FromHours(12);

    // Shorter than the hourly filing-watch cadence, so each run sees a fresh submissions feed.
    private static readonly TimeSpan FilingsTtl = TimeSpan.FromMinutes(30);

    private const int MaxConcurrentConceptFetches = 4;

    // Friendly name -> ordered us-gaap tags to try (first that returns data wins). Company taxonomies
    // differ (e.g. some report Revenues, others RevenueFromContractWithCustomer...), hence fallbacks.
    private static readonly (string Concept, string[] Tags)[] CoreConcepts =
    [
        ("Revenue", ["RevenueFromContractWithCustomerExcludingAssessedTax", "Revenues", "SalesRevenueNet"]),
        ("GrossProfit", ["GrossProfit"]),
        ("OperatingIncome", ["OperatingIncomeLoss"]),
        ("NetIncome", ["NetIncomeLoss"]),
        ("DilutedEPS", ["EarningsPerShareDiluted"]),
        ("StockholdersEquity", ["StockholdersEquity"]),
    ];

    // The same friendly concepts in the ifrs-full taxonomy, read only when a filer reports none of
    // the us-gaap ones — a foreign private issuer filing 20-F/40-F under IFRS (e.g. GRAB). Each tag
    // matches its us-gaap counterpart's meaning: NetIncome and StockholdersEquity are the parent's
    // share (excluding non-controlling interests), as NetIncomeLoss and StockholdersEquity are.
    // RevenueFromContractsWithCustomers is listed first so a filer reporting both keeps Revenue
    // (the total line) for any period they overlap — the merge below keeps the later tag's row.
    private static readonly (string Concept, string[] Tags)[] IfrsConcepts =
    [
        ("Revenue", ["RevenueFromContractsWithCustomers", "Revenue"]),
        ("GrossProfit", ["GrossProfit"]),
        ("OperatingIncome", ["ProfitLossFromOperatingActivities"]),
        ("NetIncome", ["ProfitLossAttributableToOwnersOfParent"]),
        ("DilutedEPS", ["DilutedEarningsLossPerShare"]),
        ("StockholdersEquity", ["EquityAttributableToOwnersOfParent"]),
    ];

    private readonly SemaphoreSlim tickerMapLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CachedFilings> filingsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedFundamentals> fundamentalsCache = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, string>? tickerToCik;
    private DateTimeOffset tickerMapFetchedAt;

    public async Task<IReadOnlyList<EdgarFiling>> GetRecentFilingsAsync(
        string ticker, IReadOnlyCollection<string>? formTypes, int limit, CancellationToken ct = default,
        bool surfaceProviderFailure = false)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        var forms = formTypes is { Count: > 0 }
            ? formTypes.Select(f => f.Trim().ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : DefaultFormTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var all = await GetAllFilingsAsync(upper, surfaceProviderFailure, ct);
        return all
            .Where(f => forms.Contains(f.Form))
            .Take(Math.Max(1, limit))
            .ToList();
    }

    public async Task<FundamentalsSourceResult> GetTaxonomyFundamentalsAsync(
        string ticker, string taxonomy, CancellationToken ct = default)
    {
        var upper = ticker.Trim().ToUpperInvariant();
        var concepts = taxonomy switch
        {
            FundamentalFact.UsGaapTaxonomy => CoreConcepts,
            FundamentalFact.IfrsTaxonomy => IfrsConcepts,
            _ => throw new ArgumentOutOfRangeException(nameof(taxonomy), taxonomy, "Unsupported XBRL taxonomy."),
        };

        var cacheKey = $"{upper}|{taxonomy}";
        if (fundamentalsCache.TryGetValue(cacheKey, out var hit) && DateTimeOffset.UtcNow - hit.FetchedAt < ResultTtl)
        {
            return hit.Result;
        }

        var (cik, mapFetchFailed) = await ResolveCikAsync(upper, ct);
        if (cik is null)
        {
            return mapFetchFailed
                ? FundamentalsSourceResult.Failed("EDGAR ticker->CIK map unavailable")
                : FundamentalsSourceResult.NoData(
                    "not in the SEC ticker map", FundamentalsIssuerType.NotSecRegistrant);
        }

        var fetch = await FetchTaxonomyAsync(upper, cik, taxonomy, concepts, ct);

        // A fetch that errored on every tag is an outage, not "this filer has no facts": report it as
        // a failure and cache nothing, so the next call retries and the chain does not fall through to
        // another taxonomy or provider on a bad night.
        if (fetch.Facts.Count == 0 && fetch.Failures > 0)
        {
            return FundamentalsSourceResult.Failed($"EDGAR {taxonomy} concept fetches failed for {upper}");
        }

        var issuerType = IssuerTypeFromForms(fetch.Facts.Select(f => f.Form))
            ?? IssuerTypeFromForms((await GetAllFilingsAsync(upper, surfaceProviderFailure: false, ct)).Select(f => f.Form));

        var result = fetch.Facts.Count > 0
            ? FundamentalsSourceResult.WithFacts(fetch.Facts, issuerType)
            : FundamentalsSourceResult.NoData($"no {taxonomy} facts filed", issuerType);

        // Partial failures (some tags errored) still return what was read, but are not cached.
        if (fetch.Failures == 0)
        {
            fundamentalsCache[cacheKey] = new CachedFundamentals(DateTimeOffset.UtcNow, result);
        }

        return result;
    }

    // The issuer kind told by what it files, newest form first (the submissions feed is newest-first;
    // facts are classified by any periodic form among them). Null when nothing tells.
    private static string? IssuerTypeFromForms(IEnumerable<string> forms)
    {
        foreach (var form in forms)
        {
            var upper = form.ToUpperInvariant();
            if (upper.StartsWith("10-K", StringComparison.Ordinal) || upper.StartsWith("10-Q", StringComparison.Ordinal))
            {
                return FundamentalsIssuerType.DomesticFiler;
            }

            if (upper.StartsWith("20-F", StringComparison.Ordinal) || upper.StartsWith("40-F", StringComparison.Ordinal)
                || upper.StartsWith("6-K", StringComparison.Ordinal))
            {
                return FundamentalsIssuerType.ForeignPrivateIssuer;
            }

            if (FundFormPrefixes.Any(p => upper.StartsWith(p, StringComparison.Ordinal)))
            {
                return FundamentalsIssuerType.InvestmentFund;
            }
        }

        return null;
    }

    private async Task<TaxonomyFetch> FetchTaxonomyAsync(
        string ticker, string cik, string taxonomy, (string Concept, string[] Tags)[] concepts, CancellationToken ct)
    {
        using var throttle = new SemaphoreSlim(MaxConcurrentConceptFetches);
        var tasks = concepts.Select(async spec =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await FetchConceptAsync(ticker, cik, taxonomy, spec.Concept, spec.Tags, ct);
            }
            finally
            {
                throttle.Release();
            }
        });

        var fetched = await Task.WhenAll(tasks);
        return new TaxonomyFetch(fetched.SelectMany(f => f.Facts).ToList(), fetched.Sum(f => f.Failures));
    }

    private async Task<IReadOnlyList<EdgarFiling>> GetAllFilingsAsync(
        string ticker, bool surfaceProviderFailure, CancellationToken ct)
    {
        if (filingsCache.TryGetValue(ticker, out var hit) && DateTimeOffset.UtcNow - hit.FetchedAt < FilingsTtl)
        {
            return hit.Filings;
        }

        var (cik, mapFetchFailed) = await ResolveCikAsync(ticker, ct);
        if (cik is null)
        {
            // mapFetchFailed distinguishes "EDGAR's map fetch never succeeded" (a provider
            // outage) from "the map is fine, this ticker just isn't a filer" (not a failure).
            if (surfaceProviderFailure && mapFetchFailed)
            {
                throw new EdgarProviderException($"EDGAR ticker->CIK map unavailable; cannot resolve {ticker}.");
            }

            return [];
        }

        var filings = await FetchSubmissionsAsync(ticker, cik, ct);
        if (filings is null)
        {
            if (surfaceProviderFailure)
            {
                throw new EdgarProviderException($"EDGAR submissions fetch failed for {ticker}.");
            }

            return [];
        }

        filingsCache[ticker] = new CachedFilings(DateTimeOffset.UtcNow, filings);
        return filings;
    }

    // Null on a failed fetch, so the failure is not cached and the next call retries.
    private async Task<IReadOnlyList<EdgarFiling>?> FetchSubmissionsAsync(string ticker, string cik, CancellationToken ct)
    {
        var client = httpFactory.CreateClient(HttpClientName);
        var url = $"https://data.sec.gov/submissions/CIK{cik}.json";

        try
        {
            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("filings", out var filings) ||
                !filings.TryGetProperty("recent", out var recent))
            {
                return [];
            }

            var forms = recent.GetProperty("form");
            var dates = recent.GetProperty("filingDate");
            var reportDates = recent.GetProperty("reportDate");
            var accessions = recent.GetProperty("accessionNumber");
            var primaryDocs = recent.GetProperty("primaryDocument");
            var descriptions = recent.GetProperty("primaryDocDescription");
            var isXbrl = recent.GetProperty("isXBRL");
            var cikNoPad = cik.TrimStart('0');

            var count = forms.GetArrayLength();
            var result = new List<EdgarFiling>(count);
            for (var i = 0; i < count; i++)
            {
                var filingDate = ParseDate(dates[i].GetString());
                if (filingDate is null)
                {
                    continue;
                }

                var accession = accessions[i].GetString() ?? string.Empty;
                var primaryDoc = primaryDocs[i].GetString() ?? string.Empty;
                var accessionNoDashes = accession.Replace("-", string.Empty);
                var documentUrl = string.IsNullOrEmpty(primaryDoc)
                    ? $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={cik}"
                    : $"https://www.sec.gov/Archives/edgar/data/{cikNoPad}/{accessionNoDashes}/{primaryDoc}";

                result.Add(new EdgarFiling(
                    ticker,
                    forms[i].GetString() ?? string.Empty,
                    filingDate.Value,
                    ParseDate(reportDates[i].GetString()),
                    Blank(descriptions[i].GetString()),
                    accession,
                    documentUrl,
                    isXbrl[i].ValueKind == JsonValueKind.Number && isXbrl[i].GetInt32() == 1));
            }

            // EDGAR returns recent filings newest-first already, but sort defensively.
            return result.OrderByDescending(f => f.FilingDate).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "EDGAR submissions fetch failed for {Ticker}", ticker);
            return null;
        }
    }

    private async Task<ConceptFetch> FetchConceptAsync(
        string ticker, string cik, string taxonomy, string concept, string[] tags, CancellationToken ct)
    {
        var client = httpFactory.CreateClient(HttpClientName);

        // Companies change XBRL tags over time (e.g. Revenues -> RevenueFromContractWith...), so a
        // single tag can hold only an old slice. Merge datapoints from every candidate tag, then
        // collapse duplicate periods — this gives continuous coverage regardless of which tag is current.
        var merged = new List<FundamentalFact>();
        var failures = 0;
        foreach (var tag in tags)
        {
            var url = $"https://data.sec.gov/api/xbrl/companyconcept/CIK{cik}/{taxonomy}/{tag}.json";
            try
            {
                using var response = await client.GetAsync(url, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    continue; // company doesn't report under this tag — try the next fallback.
                }

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                merged.AddRange(ParseConcept(ticker, cik, taxonomy, concept, url, doc.RootElement, DateTimeOffset.UtcNow));
            }
            // An HttpClient timeout is a TaskCanceledException too: only the caller's own cancellation propagates.
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning(ex, "EDGAR concept {Tag} fetch failed for {Ticker}", tag, ticker);
            }
        }

        // Collapse restatements + cross-tag overlap: keep one row per (period, fiscal period), newest first.
        var facts = merged
            .GroupBy(f => (f.PeriodEnd, f.FiscalPeriod))
            .Select(g => g.Last())
            .OrderByDescending(f => f.PeriodEnd)
            .ToList();
        return new ConceptFetch(facts, failures);
    }

    private static IReadOnlyList<FundamentalFact> ParseConcept(
        string ticker, string cik, string taxonomy, string concept, string conceptUrl, JsonElement root,
        DateTimeOffset ingestedAt)
    {
        var provider = taxonomy == FundamentalFact.IfrsTaxonomy
            ? FundamentalsSourceNames.SecEdgarIfrs
            : FundamentalsSourceNames.SecEdgarUsGaap;
        var cikNoPad = cik.TrimStart('0');
        var label = root.TryGetProperty("label", out var labelProp) ? labelProp.GetString() ?? concept : concept;

        if (!root.TryGetProperty("units", out var units) || units.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        // Prefer USD, then per-share, then whatever the concept reports in.
        var unitProperty = units.EnumerateObject()
            .OrderBy(u => u.Name switch { "USD" => 0, "USD/shares" => 1, _ => 2 })
            .First();

        var facts = new List<FundamentalFact>();
        foreach (var point in unitProperty.Value.EnumerateArray())
        {
            var end = ParseDate(point.TryGetProperty("end", out var e) ? e.GetString() : null);
            if (end is null || !point.TryGetProperty("val", out var val) || !val.TryGetDecimal(out var value))
            {
                continue;
            }

            var form = point.TryGetProperty("form", out var formProp) ? formProp.GetString() ?? string.Empty : string.Empty;

            // Keep only the primary periodic statements. The same figure is echoed in proxies (DEF 14A),
            // registration statements, etc. — including those double-counts and adds noise.
            if (!IsPeriodicStatement(form))
            {
                continue;
            }

            // The filing the figure was read from: its EDGAR index page (from the datapoint's accession
            // number), or the concept endpoint itself when EDGAR gives no accession.
            var accession = point.TryGetProperty("accn", out var accn) ? accn.GetString() : null;
            var documentUrl = string.IsNullOrEmpty(accession)
                ? conceptUrl
                : $"https://www.sec.gov/Archives/edgar/data/{cikNoPad}/{accession.Replace("-", string.Empty)}/{accession}-index.htm";

            facts.Add(new FundamentalFact(
                ticker,
                concept,
                label,
                unitProperty.Name,
                value,
                end.Value,
                point.TryGetProperty("fp", out var fp) ? fp.GetString() : null,
                point.TryGetProperty("fy", out var fy) && fy.ValueKind == JsonValueKind.Number ? fy.GetInt32() : null,
                form,
                taxonomy,
                new SourceProvenance(provider, documentUrl, ingestedAt)));
        }

        return facts;
    }

    // The audited/reviewed periodic financials and their amendments (…/A): 10-K / 10-Q for domestic
    // filers, 20-F / 40-F annual reports for foreign private issuers. A 6-K (an FPI's interim
    // furnishing) is left out — EDGAR tags its facts with no fiscal period, and it is not a
    // periodic statement in the 10-Q sense.
    private static bool IsPeriodicStatement(string form)
        => form.StartsWith("10-K", StringComparison.OrdinalIgnoreCase)
            || form.StartsWith("10-Q", StringComparison.OrdinalIgnoreCase)
            || form.StartsWith("20-F", StringComparison.OrdinalIgnoreCase)
            || form.StartsWith("40-F", StringComparison.OrdinalIgnoreCase);

    // MapFetchFailed is true only when the ticker->CIK map was never fetched successfully (no
    // cached copy to fall back on) — a provider outage, distinct from the ticker legitimately
    // not being an EDGAR filer once a real map is in hand.
    private async Task<(string? Cik, bool MapFetchFailed)> ResolveCikAsync(string ticker, CancellationToken ct)
    {
        var map = await GetTickerMapAsync(ct);
        if (map is null)
        {
            return (null, true);
        }

        return (map.TryGetValue(ticker, out var cik) ? cik : null, false);
    }

    private async Task<IReadOnlyDictionary<string, string>?> GetTickerMapAsync(CancellationToken ct)
    {
        if (tickerToCik is not null && DateTimeOffset.UtcNow - tickerMapFetchedAt < TickerMapTtl)
        {
            return tickerToCik;
        }

        await tickerMapLock.WaitAsync(ct);
        try
        {
            if (tickerToCik is not null && DateTimeOffset.UtcNow - tickerMapFetchedAt < TickerMapTtl)
            {
                return tickerToCik;
            }

            var client = httpFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(TickerMapUrl, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                var row = entry.Value;
                if (!row.TryGetProperty("ticker", out var t) || !row.TryGetProperty("cik_str", out var c))
                {
                    continue;
                }

                var symbol = t.GetString();
                if (!string.IsNullOrEmpty(symbol) && c.ValueKind == JsonValueKind.Number)
                {
                    // EDGAR paths use the 10-digit zero-padded CIK.
                    map[symbol] = c.GetInt64().ToString(CultureInfo.InvariantCulture).PadLeft(10, '0');
                }
            }

            tickerToCik = map;
            tickerMapFetchedAt = DateTimeOffset.UtcNow;
            return tickerToCik;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "EDGAR ticker->CIK map fetch failed");
            return tickerToCik; // fall back to a stale map if we have one
        }
        finally
        {
            tickerMapLock.Release();
        }
    }

    private static DateOnly? ParseDate(string? value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string Blank(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value;

    private readonly record struct CachedFilings(DateTimeOffset FetchedAt, IReadOnlyList<EdgarFiling> Filings);

    private readonly record struct CachedFundamentals(DateTimeOffset FetchedAt, FundamentalsSourceResult Result);

    private readonly record struct ConceptFetch(IReadOnlyList<FundamentalFact> Facts, int Failures);

    private readonly record struct TaxonomyFetch(IReadOnlyList<FundamentalFact> Facts, int Failures);
}
