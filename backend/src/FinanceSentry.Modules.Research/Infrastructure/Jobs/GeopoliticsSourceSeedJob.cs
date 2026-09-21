namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using System.Text.RegularExpressions;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily Hangfire job (N2, ledger-heartbeat design): registers a Google News RSS query source per
/// thesis for the geopolitics/policy terms its <c>ThesisText</c> mentions (sanctions, tariffs, export
/// controls, rulings, regulation, ...). Q3 option A settles how — this goes through the existing
/// <see cref="RegisterThesisSourceCommand"/> (<c>register_thesis_source</c>) with
/// <c>Kind = "Rss"</c>, adding no new <see cref="Domain.NewsSourceKind"/>. GDELT is the named fallback
/// and is deliberately not built here. A thesis whose text mentions none of the configured terms is
/// skipped, keeping volume low (~0.2 fires/day) — the term match, not every thesis, is the source of a
/// registration. Registration only adds a <c>news_sources</c> row; the 30-min ingestion sweep
/// (<see cref="NewsIngestionJob.IngestRegisteredSourcesAsync"/>) and the material-cluster detector
/// (<see cref="NewsMaterialityJob"/>, N1) do the rest through the existing RSS pipeline, so a Google
/// News hiccup — a missing field, an empty feed, a slow response, a malformed entry — is already
/// handled the same way any other registered RSS source's failure is: logged, retried, and only
/// alerted through <see cref="NewsIngestionJob"/>'s own SyncFailure path after the health tracker's
/// consecutive-failure threshold, never a job failure here.
/// Each source's URL carries its thesis id (as a fragment, never sent to Google), so two theses on the
/// same ticker get separate sources instead of overwriting each other's owner. A URL that is already
/// registered is skipped rather than re-registered, so re-running this job daily is a no-op once a
/// thesis's geopolitics source exists — it never re-enables a source the health tracker retired.
/// Terms match whole words only (optionally pluralised), so "war" never matches "software".
/// </summary>
public sealed class GeopoliticsSourceSeedJob(
    ResearchDbContext research,
    INewsSourceRepository sources,
    ICommandHandler<RegisterThesisSourceCommand, RegisteredSourceDto> registerSource,
    ILogger<GeopoliticsSourceSeedJob> logger)
{
    private const int MaxTermsPerQuery = 3;

    /// <summary>
    /// Geopolitics/policy terms (report §5.3, N2 examples: Ukraine ceasefire, sanctions, export
    /// controls, SEC crypto rulings, stablecoin bill). A thesis's query is derived from whichever of
    /// these its <c>ThesisText</c> mentions — never a fixed universal query, so a thesis with no
    /// geopolitical exposure never gets a source registered for it.
    /// </summary>
    private static readonly string[] GeopoliticsTerms =
    [
        "sanction", "tariff", "export control", "ceasefire", "embargo",
        "war", "conflict", "ruling", "regulation", "regulatory", "SEC", "stablecoin", "bill",
    ];

    private static readonly Regex[] GeopoliticsTermPatterns =
        [.. GeopoliticsTerms.Select(term => new Regex(
            $@"\b{Regex.Escape(term)}s?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))];

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var theses = await research.Theses.AsNoTracking()
            .Select(t => new { t.Id, t.Ticker, t.ThesisText })
            .ToListAsync(ct);

        var registered = 0;
        foreach (var thesis in theses)
        {
            var terms = MatchTerms(thesis.ThesisText);
            if (terms.Count == 0)
            {
                continue;
            }

            var url = BuildGoogleNewsRssUrl(thesis.Id, thesis.Ticker, terms);
            try
            {
                if (await sources.GetByUrlAsync(url, ct) is not null)
                {
                    continue;
                }

                await registerSource.Handle(
                    new RegisterThesisSourceCommand(
                        thesis.Id,
                        $"Google News: {thesis.Ticker} geopolitics",
                        url,
                        "Rss",
                        terms),
                    ct);
                registered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Registration is a DB write through the existing command — a single thesis's failure
                // (e.g. a transient DB hiccup) must not stop the rest of the sweep or fail the job.
                logger.LogWarning(ex, "GeopoliticsSourceSeed: failed to register source for thesis {ThesisId}", thesis.Id);
            }
        }

        logger.LogInformation("GeopoliticsSourceSeedJob registered {Count} new geopolitics sources", registered);
    }

    private static List<string> MatchTerms(string thesisText)
        => [.. GeopoliticsTerms
            .Where((_, i) => GeopoliticsTermPatterns[i].IsMatch(thesisText))
            .Take(MaxTermsPerQuery)];

    private static string BuildGoogleNewsRssUrl(Guid thesisId, string ticker, IReadOnlyList<string> terms)
    {
        var query = $"{ticker} ({string.Join(" OR ", terms)})";
        return $"https://news.google.com/rss/search?q={Uri.EscapeDataString(query)}&hl=en-US&gl=US&ceid=US:en#thesis={thesisId:N}";
    }
}
