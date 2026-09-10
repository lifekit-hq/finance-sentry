namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Scheduled FR-008 machine scan (019 US2): nominates candidates from 018 market structure by the
/// deterministic <see cref="ScanNominationRules"/>, re-ranks a bounded shortlist of those by
/// combined quality x momentum (FR-006 — EDGAR grade against universe RS, so a name outside the
/// book can win a slot), and scores the survivors through the same pipeline as user nominations,
/// so 020 can compare hit rates across the two sources. Runs after Radar's nightly compute. One
/// ticker failing never aborts the run (FR-013); an empty 018 read aborts with a recorded error;
/// zero nominations is a valid, silent outcome.
/// </summary>
public sealed class OpportunityScanJob(
    IMarketStructureReader structureReader,
    ISecEdgarService secEdgar,
    Domain.Repositories.IIpsRepository ipsRepo,
    ICommandHandler<ScoreCandidateCommand, ScoreCandidateResult> scorer,
    IOptions<OpportunityOptions> options,
    ILogger<OpportunityScanJob> logger)
{
    private readonly OpportunityOptions _options = options.Value;

    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var universe = await structureReader.GetUniverseStructuresAsync(ct);
        if (universe.Count == 0)
        {
            logger.LogError("Opportunity scan aborted: no 018 structure data (empty universe read — ingestion outage?)");
            return;
        }

        var nominations = ScanNominationRules.Evaluate(universe, _options);
        // The shortlist bounds the EDGAR fan-out, never the nomination count: a shortlist configured
        // below the cap would silently score fewer candidates than the cap allows.
        var shortlistSize = Math.Max(_options.ScanQualityShortlistSize, _options.ScanMaxNominationsPerRun);
        var shortlist = nominations.Take(shortlistSize).ToList();
        var ranked = ScanNominationRules.RankByQualityMomentum(
            shortlist, await GradeFundamentalsAsync(shortlist, ct), _options);
        var capped = ranked.Take(_options.ScanMaxNominationsPerRun).ToList();
        if (capped.Count < nominations.Count)
        {
            logger.LogWarning(
                "Opportunity scan dropped {Dropped} nomination(s) — cap {Cap}, graded shortlist {Shortlist}: " +
                "{DroppedTickers}",
                nominations.Count - capped.Count,
                _options.ScanMaxNominationsPerRun,
                shortlistSize,
                string.Join(", ", ranked.Skip(capped.Count).Select(n => n.Ticker)
                    .Concat(nominations.Skip(shortlist.Count).Select(n => n.Ticker))));
        }

        var userIds = await ipsRepo.GetUserIdsWithCurrentIpsAsync(ct);
        var scored = 0;
        var newCandidates = 0;
        var errors = 0;

        foreach (var userId in userIds)
        {
            foreach (var nomination in capped)
            {
                try
                {
                    var result = await scorer.Handle(
                        new ScoreCandidateCommand(
                            userId,
                            nomination.Ticker,
                            DecisionNote: null,
                            Source: CandidateSource.Scan,
                            NominationReasons: nomination.Reasons),
                        ct);
                    scored++;
                    if (result.IsNewCandidate)
                    {
                        newCandidates++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors++;
                    logger.LogError(ex,
                        "Opportunity scan failed to score {Ticker} for user {UserId}", nomination.Ticker, userId);
                }
            }
        }

        logger.LogInformation(
            "Opportunity scan run: universe {Universe}, nominated {Nominated} (graded {Graded}, capped {Capped}), " +
            "users {Users}, scored {Scored}, new candidates {New}, errors {Errors}; nominees {Nominees}",
            universe.Count, nominations.Count, shortlist.Count, capped.Count, userIds.Count, scored, newCandidates,
            errors,
            string.Join(", ", capped.Select(FormatNominee)));
    }

    /// <summary>Nominee line for the run summary — which half of the score each survivor won on.</summary>
    private static string FormatNominee(ScanCandidateRank rank)
        => FormattableString.Invariant(
            $"{rank.Ticker}(quality {rank.QualityScore}, rs {rank.RsPercentile}, combined {rank.CombinedScore})");

    /// <summary>
    /// EDGAR fundamentals grade per shortlisted ticker. A ticker EDGAR cannot answer for — crypto,
    /// ETFs, a delisted filer, an upstream failure — grades null and falls back to momentum-only
    /// standing; one bad ticker never aborts the run (FR-013).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, int?>> GradeFundamentalsAsync(
        IReadOnlyList<ScanNomination> shortlist, CancellationToken ct)
    {
        var grades = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var nomination in shortlist)
        {
            try
            {
                var facts = await secEdgar.GetFundamentalsAsync(
                    nomination.Ticker, FundamentalsScorer.FactsPerConcept, ct);
                grades[nomination.Ticker] = FundamentalsScorer.Score(facts).Score;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                grades[nomination.Ticker] = null;
                logger.LogWarning(ex,
                    "Opportunity scan could not grade fundamentals for {Ticker}; ranking it on momentum only",
                    nomination.Ticker);
            }
        }

        return grades;
    }
}
