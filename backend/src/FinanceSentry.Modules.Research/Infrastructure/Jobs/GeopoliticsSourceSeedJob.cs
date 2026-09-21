namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
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
/// <see cref="RegisterThesisSourceCommand"/> is idempotent by URL, so re-running this job daily is a
/// no-op once a thesis's geopolitics source already exists.
/// </summary>
public sealed class GeopoliticsSourceSeedJob(
    ResearchDbContext research,
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
        "sanctions", "tariff", "tariffs", "export control", "export controls", "ceasefire", "embargo",
        "war", "conflict", "ruling", "regulation", "regulatory", "SEC", "stablecoin", "bill",
    ];

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

            try
            {
                await registerSource.Handle(
                    new RegisterThesisSourceCommand(
                        thesis.Id,
                        $"Google News: {thesis.Ticker} geopolitics",
                        BuildGoogleNewsRssUrl(thesis.Ticker, terms),
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

        logger.LogInformation("GeopoliticsSourceSeedJob registered/refreshed {Count} geopolitics sources", registered);
    }

    private static List<string> MatchTerms(string thesisText)
        => [.. GeopoliticsTerms
            .Where(term => thesisText.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(MaxTermsPerQuery)];

    private static string BuildGoogleNewsRssUrl(string ticker, IReadOnlyList<string> terms)
    {
        var query = $"{ticker} ({string.Join(" OR ", terms)})";
        return $"https://news.google.com/rss/search?q={Uri.EscapeDataString(query)}&hl=en-US&gl=US&ceid=US:en";
    }
}
