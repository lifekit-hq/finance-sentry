namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// Reads one user's coverage inputs once (<see cref="CoverageSnapshot"/>). Everything here is a read of data the
/// platform already holds - holdings, watchlist, candidates and their scores, theses and their events, the stored
/// verdict readings - so no provider is called; the evaluator adds the two provider-backed facts (a catalyst date,
/// a correlation) only where a rule needs them.
/// </summary>
public sealed class CoverageSnapshotReader(
    IBrokerageHoldingsReader brokerage,
    IWatchlistReader watchlist,
    ICandidateRepository candidates,
    ICandidateScoreRepository scores,
    IThesisRepository theses,
    IThesisEventRepository thesisEvents,
    ICoverageRepository coverage,
    IOptions<CoverageOptions> options)
{
    private const string EquityInstrumentType = "STK";

    private readonly CoverageOptions _options = options.Value;

    /// <param name="flagsOnly">
    /// Reads only who is covered, held, watchlisted and carrying a thesis, skipping candidates, scores, events and
    /// verdict readings (one query each, per candidate): enough for a list, not for the rules.
    /// </param>
    public async Task<CoverageSnapshot> ReadAsync(
        Guid userId, DateTimeOffset asOf, CancellationToken ct = default, bool flagsOnly = false)
    {
        var today = DateOnly.FromDateTime(asOf.UtcDateTime);
        var members = await coverage.ListMembersUnscopedAsync(userId, ct);
        var holdings = await brokerage.GetHoldingsAsync(userId, ct);

        var held = holdings
            .Where(h => string.Equals(h.InstrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase))
            .Select(h => Normalize(h.Symbol))
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var watched = (await watchlist.ListTickersAsync(userId, ct))
            .Select(Normalize)
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allTheses = await theses.ListUnscopedAsync(userId, ct);
        var active = allTheses.Where(t => t.BrokenAt is null && !string.IsNullOrWhiteSpace(t.Ticker)).ToList();
        var horizon = today.AddDays(_options.CatalystLookaheadDays);
        var thesisCatalysts = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        foreach (var thesis in active)
        {
            foreach (var catalyst in (thesis.Catalysts ?? []).Where(c => c.Date >= today && c.Date <= horizon))
            {
                var ticker = Normalize(thesis.Ticker);
                if (!thesisCatalysts.TryGetValue(ticker, out var existing) || catalyst.Date < existing)
                {
                    thesisCatalysts[ticker] = catalyst.Date;
                }
            }
        }

        var events = flagsOnly ? [] : await thesisEvents.ListUnscopedAsync(userId, null, ct);
        var lastEvents = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events.Where(e => e.EventType != ThesisEventType.Snapshot))
        {
            Latest(lastEvents, Normalize(e.Ticker), e.Timestamp);
        }

        var allCandidates = flagsOnly ? [] : await candidates.ListAllUnscopedAsync(userId, ct);
        var noteCandidates = events
            .Where(e => e.SubjectType == ThesisSubjectType.Candidate && e.EventType == ThesisEventType.Created
                && !string.IsNullOrWhiteSpace(e.DecisionNote))
            .Select(e => e.SubjectId)
            .ToHashSet();

        var byTicker = new Dictionary<string, List<CoverageCandidateFacts>>(StringComparer.OrdinalIgnoreCase);
        var activeIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var latestScores = new Dictionary<string, CandidateScore>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in allCandidates)
        {
            var ticker = Normalize(candidate.Ticker);
            if (!byTicker.TryGetValue(ticker, out var list))
            {
                byTicker[ticker] = list = [];
            }

            list.Add(new CoverageCandidateFacts(
                candidate.Id, candidate.Source, candidate.Status, candidate.CreatedAt, noteCandidates.Contains(candidate.Id)));
            if (candidate.Status == CandidateStatus.Active)
            {
                activeIds[ticker] = candidate.Id;
            }

            var score = await scores.LatestForCandidateAsync(candidate.Id, ct);
            if (score is not null)
            {
                if (!latestScores.TryGetValue(ticker, out var current) || score.ScoredAt > current.ScoredAt)
                {
                    latestScores[ticker] = score;
                }

                Latest(lastEvents, ticker, score.ScoredAt);
            }
        }

        var since = today.AddDays(-Math.Max(_options.BuyWindowDays, _options.VerdictLookbackDays));
        var verdicts = new Dictionary<string, List<CoverageVerdictDay>>(StringComparer.OrdinalIgnoreCase);
        foreach (var observation in flagsOnly ? [] : await coverage.ListObservationsUnscopedAsync(userId, since, ct))
        {
            var ticker = Normalize(observation.Ticker);
            if (!verdicts.TryGetValue(ticker, out var days))
            {
                verdicts[ticker] = days = [];
            }

            days.Add(new CoverageVerdictDay(observation.ObservedOn, observation.Verdict));
        }

        return new CoverageSnapshot
        {
            AsOf = asOf,
            Members = members,
            Holdings = holdings,
            Held = held,
            Watchlist = watched,
            ThesisTickers = active.Select(t => Normalize(t.Ticker)).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ThesisCatalysts = thesisCatalysts,
            Candidates = byTicker,
            LatestScores = latestScores,
            LastEvents = lastEvents,
            Verdicts = verdicts,
            ActiveCandidateIds = activeIds,
        };
    }

    public static string Normalize(string ticker) => ticker.Trim().ToUpperInvariant();

    private static void Latest(Dictionary<string, DateTimeOffset> map, string ticker, DateTimeOffset at)
    {
        if (!map.TryGetValue(ticker, out var current) || at > current)
        {
            map[ticker] = at;
        }
    }
}
