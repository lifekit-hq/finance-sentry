namespace FinanceSentry.Modules.Research.Application.Services.Catalyst;

using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>What one filing scan found and how.</summary>
/// <param name="Filings">The 8-K filings on universe names that carry a watched item, dated inside the window, one per (ticker, accession).</param>
/// <param name="Source">The feed the filings came from: <see cref="CatalystRules.SourceFullText"/> or, when it failed, <see cref="CatalystRules.SourceSubmissions"/>.</param>
/// <param name="FallbackReason">Why the full-text search was not used; null when it was.</param>
/// <param name="TickersRead">Tickers the fallback read (0 on the full-text path).</param>
public sealed record CatalystScan(
    IReadOnlyList<CatalystFiling> Filings, string Source, string? FallbackReason, int TickersRead);

/// <summary>
/// Finds the watched-item 8-Ks filed on universe names (discovery funnel stage 7): EDGAR full-text search first, the per-ticker
/// submissions feed the app already reads (<see cref="ISecEdgarService"/>, with its cache and ticker map) when the full-text
/// search fails. Both paths are paced to the SEC's fair-access rate through the one shared pacer. A fallback that cannot read a
/// single ticker raises <see cref="CatalystFeedException"/>: the run fails loudly instead of reporting a quiet day.
/// </summary>
public sealed class CatalystFilingScanner(
    IEdgarFullTextSearch fullText,
    ISecEdgarService edgar,
    [FromKeyedServices(CatalystPacers.Sec)] RequestPacer pacer,
    IOptions<CatalystOptions> options,
    ILogger<CatalystFilingScanner> logger)
{
    private const int SubmissionsReadLimit = 10;
    private const int MinutesPerDay = 24 * 60;

    private readonly CatalystOptions _options = options.Value;

    public async Task<CatalystScan> ScanAsync(
        IReadOnlySet<string> universe, DateOnly from, DateOnly to, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var found = new Dictionary<(string Ticker, string Accession), CatalystFiling>();
        string? failure = null;
        try
        {
            foreach (var item in _options.Items.Distinct(StringComparer.Ordinal))
            {
                foreach (var filing in await fullText.SearchAsync(item, from, to, ct))
                {
                    Keep(found, filing, universe, from, to);
                }
            }
        }
        catch (CatalystFeedException ex)
        {
            failure = ex.Message;
            logger.LogWarning(ex, "Catalyst scan: EDGAR full-text search failed, falling back to the submissions feed");
        }

        if (failure is null)
        {
            return new CatalystScan(Ordered(found), CatalystRules.SourceFullText, null, 0);
        }

        var read = await FallbackAsync(universe, from, to, asOf, found, ct);
        return new CatalystScan(Ordered(found), CatalystRules.SourceSubmissions, failure, read);
    }

    private async Task<int> FallbackAsync(
        IReadOnlySet<string> universe, DateOnly from, DateOnly to, DateTimeOffset asOf,
        Dictionary<(string Ticker, string Accession), CatalystFiling> found, CancellationToken ct)
    {
        var ordered = universe.Order(StringComparer.Ordinal).ToList();
        var slice = TickerRotation.Slice(ordered, _options.FallbackMaxTickers, TickerRotation.RunIndex(asOf, MinutesPerDay));
        if (slice.Count < ordered.Count)
        {
            logger.LogWarning(
                "Catalyst scan fallback reads {Read} of {Universe} universe names today (Catalyst:FallbackMaxTickers); the rest follow on the next runs",
                slice.Count, ordered.Count);
        }

        var read = 0;
        var failed = 0;
        string? lastError = null;
        foreach (var ticker in slice)
        {
            ct.ThrowIfCancellationRequested();
            await pacer.WaitAsync(ct);
            IReadOnlyList<EdgarFiling> filings;
            try
            {
                filings = await edgar.GetRecentFilingsAsync(
                    ticker, [CatalystRules.Form8K], SubmissionsReadLimit, ct, surfaceProviderFailure: true);
            }
            catch (EdgarProviderException ex)
            {
                failed++;
                lastError = ex.Message;
                logger.LogWarning(ex, "Catalyst scan fallback: the submissions read failed for {Ticker}", ticker);
                continue;
            }

            read++;
            foreach (var filing in filings.Where(f => f.Items is { Count: > 0 }))
            {
                Keep(found, new CatalystFiling(
                    ticker, filing.AccessionNumber, filing.Form, filing.Items!, filing.FilingDate, filing.DocumentUrl,
                    CatalystRules.SourceSubmissions), universe, from, to);
            }
        }

        if (read == 0 && failed > 0)
        {
            throw new CatalystFeedException(
                $"Neither EDGAR feed could be read: the full-text search failed and all {failed} submissions reads failed ({lastError}).");
        }

        return read;
    }

    /// <summary>Adds the filing when it is an 8-K on a universe name, dated in the window, carrying a watched item.</summary>
    private void Keep(
        Dictionary<(string Ticker, string Accession), CatalystFiling> found, CatalystFiling filing,
        IReadOnlySet<string> universe, DateOnly from, DateOnly to)
    {
        if (!string.Equals(filing.Form, CatalystRules.Form8K, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(filing.Accession)
            || filing.FilingDate < from || filing.FilingDate > to
            || !universe.Contains(filing.Ticker)
            || CatalystRules.WatchedItems(filing.Items, _options.Items).Count == 0)
        {
            return;
        }

        found.TryAdd((filing.Ticker, filing.Accession), filing);
    }

    private static IReadOnlyList<CatalystFiling> Ordered(Dictionary<(string Ticker, string Accession), CatalystFiling> found)
        => [.. found.Values.OrderBy(f => f.Ticker, StringComparer.Ordinal).ThenBy(f => f.Accession, StringComparer.Ordinal)];
}
