namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using System.Globalization;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>What one evaluation changed.</summary>
public sealed record CoverageRunSummary(int Added, int Removed, int Updated, int Deferred, int Observations, int Members)
{
    public static CoverageRunSummary Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public CoverageRunSummary Plus(CoverageRunSummary other) => new(
        Added + other.Added, Removed + other.Removed, Updated + other.Updated, Deferred + other.Deferred,
        Observations + other.Observations, Members + other.Members);
}

/// <summary>
/// The coverage evaluation (discovery funnel stage 1): seeds the universe from holdings and the watchlist, applies
/// the add and remove rules (<see cref="CoverageRules"/>) to the rest, ranks cap-bound adds with the D1 correlation
/// penalty, and journals every add and remove with the rule that fired. Deterministic and offline-friendly: the only
/// provider reads are the scorecard behind a verdict reading, the earnings date of an idle name and the stored-bar
/// correlation, and none of them is a model call.
/// </summary>
public sealed class CoverageEvaluator(
    CoverageSnapshotReader snapshots,
    ICoverageRepository coverage,
    ICoverageVerdictSource verdicts,
    IReturnCorrelationReader correlation,
    IEarningsCalendarService earnings,
    IBankingTotalsReader banking,
    IIpsRepository ips,
    IOptions<CoverageOptions> options,
    IOptions<OpportunityOptions> opportunityOptions,
    ILogger<CoverageEvaluator> logger)
{
    private readonly CoverageOptions _options = options.Value;
    private readonly OpportunityOptions _opportunity = opportunityOptions.Value;

    /// <summary>Evaluates every user that has candidates, an IPS, banking activity or members. One user failing never skips the rest, but fails the run once all are done.</summary>
    public async Task<CoverageRunSummary> EvaluateAllAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        var users = new HashSet<Guid>(await coverage.ListUserIdsUnscopedAsync(ct));
        users.UnionWith(await banking.GetActiveUserIdsAsync(ct));
        users.UnionWith(await ips.GetUserIdsWithCurrentIpsUnscopedAsync(ct));

        var total = CoverageRunSummary.Empty;
        var failed = new List<Guid>();
        foreach (var userId in users)
        {
            try
            {
                total = total.Plus(await EvaluateUserAsync(userId, asOf, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed.Add(userId);
                logger.LogError(ex, "Coverage evaluation failed for user {UserId}", userId);
            }
        }

        var pruned = await coverage.PruneObservationsUnscopedAsync(
            DateOnly.FromDateTime(asOf.UtcDateTime).AddDays(-_options.ObservationRetentionDays), ct);

        logger.LogInformation(
            "Coverage evaluation: users {Users}, added {Added}, removed {Removed}, updated {Updated}, deferred by the cap " +
            "{Deferred}, verdict readings {Observations}, pruned {Pruned}, members {Members}, failed users {Failed}",
            users.Count, total.Added, total.Removed, total.Updated, total.Deferred, total.Observations, pruned,
            total.Members, failed.Count);

        if (failed.Count > 0)
        {
            throw new InvalidOperationException(
                $"Coverage evaluation failed for {failed.Count} of {users.Count} user(s); the others were saved.");
        }

        return total;
    }

    public async Task<CoverageRunSummary> EvaluateUserAsync(Guid userId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var snapshot = await snapshots.ReadAsync(userId, asOf, ct);
        var observations = await RecordVerdictsAsync(userId, snapshot, ct);

        var correlations = new Dictionary<string, (decimal? Value, int Penalty)>(StringComparer.OrdinalIgnoreCase);
        var book = snapshot.Holdings.Select(h => new CorrelationReference(h.Symbol, h.UsdValue)).ToList();

        var added = new List<CoverageMember>();
        var updated = new List<CoverageMember>();
        var removed = new List<CoverageMember>();
        var journal = new List<CoverageJournalEntry>();

        // Members first: refresh their facts and apply the remove rules.
        var catalysts = await NextEarningsAsync(snapshot, asOf, ct);
        var keep = new List<CoverageMember>();
        foreach (var member in snapshot.Members)
        {
            var facts = MemberFacts(snapshot, member, catalysts);
            var (corr, penalty) = await CorrelationAsync(correlations, member.Ticker, book, ct);
            facts = facts with { CorrelationToBook = corr, CorrelationPenalty = penalty };

            if (CoverageRules.EvaluateRemove(facts, _options, asOf, member.AddedAt) is { } decision)
            {
                removed.Add(member);
                journal.Add(Entry(userId, member.Ticker, CoverageAction.Removed, decision.Rule, member.AddedReason,
                    decision.Detail, asOf, corr, null));
                continue;
            }

            keep.Add(member);
            if (Refresh(member, facts, snapshot))
            {
                updated.Add(member);
            }
        }

        // Then the names not yet covered: pinned ones always join, ranked ones compete for the free slots.
        var removedTickers = removed.Select(m => m.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var covered = keep.Select(m => m.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contenders = snapshot.Held
            .Concat(snapshot.Watchlist)
            .Concat(snapshot.ActiveCandidateIds.Keys)
            .Where(t => !covered.Contains(t) && !removedTickers.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

        var ranked = new List<(CoverageTickerFacts Facts, CoverageAddDecision Decision, int Rank)>();
        foreach (var ticker in contenders)
        {
            var facts = snapshot.Facts(ticker, null, false, null, 0);
            var pinnedOrRanked = CoverageRules.EvaluateAdd(facts, _options, asOf);
            if (pinnedOrRanked is null)
            {
                continue;
            }

            var (corr, penalty) = await CorrelationAsync(correlations, ticker, book, ct);
            facts = facts with { CorrelationToBook = corr, CorrelationPenalty = penalty };
            if (pinnedOrRanked.Pinned)
            {
                added.Add(NewMember(userId, facts, pinnedOrRanked, snapshot, asOf));
                journal.Add(Entry(userId, ticker, CoverageAction.Added, pinnedOrRanked.Rule, pinnedOrRanked.Reason,
                    pinnedOrRanked.Detail, asOf, corr, null));
            }
            else
            {
                ranked.Add((facts, pinnedOrRanked, CoverageRules.Rank(facts)));
            }
        }

        var slots = Math.Max(0, _options.MaxMembers - (keep.Count + added.Count));
        var winners = ranked
            .OrderByDescending(r => r.Rank)
            .ThenBy(r => r.Facts.Ticker, StringComparer.Ordinal)
            .Take(slots)
            .ToList();
        foreach (var (facts, decision, rank) in winners)
        {
            added.Add(NewMember(userId, facts, decision, snapshot, asOf));
            journal.Add(Entry(userId, facts.Ticker, CoverageAction.Added, decision.Rule, decision.Reason,
                RankedDetail(decision, facts, rank), asOf, facts.CorrelationToBook, rank));
        }

        var deferred = ranked.Count - winners.Count;
        if (deferred > 0)
        {
            logger.LogWarning(
                "Coverage: the cap of {Cap} left {Deferred} qualifying name(s) out for user {UserId}: {Tickers}",
                _options.MaxMembers, deferred, userId,
                string.Join(", ", ranked.Except(winners).Select(r => $"{r.Facts.Ticker}({r.Rank})")));
        }

        if (added.Count + keep.Count > _options.MaxMembers)
        {
            AnnotateOverCap(journal, added);
        }

        await coverage.SaveEvaluationAsync(userId, added, updated, removed, journal, ct);

        return new CoverageRunSummary(
            added.Count, removed.Count, updated.Count, deferred, observations, keep.Count + added.Count);
    }

    /// <summary>
    /// Records today's verdict for each Active candidate that was scored inside the window, once per day: members
    /// first, then the most recently scored, up to <see cref="CoverageOptions.MaxVerdictReadsPerRun"/>.
    /// </summary>
    private async Task<int> RecordVerdictsAsync(Guid userId, CoverageSnapshot snapshot, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(snapshot.AsOf.UtcDateTime);
        var since = snapshot.AsOf.AddDays(-_options.BuyWindowDays);
        var memberTickers = snapshot.Members.Select(m => m.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var due = snapshot.ActiveCandidateIds
            .Where(c => snapshot.LatestScores.TryGetValue(c.Key, out var score) && score.ScoredAt >= since)
            .Where(c => !(snapshot.Verdicts.GetValueOrDefault(c.Key)?.Any(v => v.Day == today) ?? false))
            .OrderByDescending(c => memberTickers.Contains(c.Key))
            .ThenByDescending(c => snapshot.LatestScores[c.Key].ScoredAt)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();

        if (due.Count > _options.MaxVerdictReadsPerRun)
        {
            logger.LogWarning(
                "Coverage: {Due} candidates need a verdict reading but the run reads at most {Max}; the rest wait for the next run",
                due.Count, _options.MaxVerdictReadsPerRun);
            due = due.Take(_options.MaxVerdictReadsPerRun).ToList();
        }

        var recorded = 0;
        foreach (var (ticker, candidateId) in due)
        {
            var reading = await verdicts.ReadAsync(ticker, ct);
            if (reading is null)
            {
                continue;
            }

            var observation = new CoverageVerdictObservation
            {
                UserId = userId,
                Ticker = ticker,
                ObservedOn = today,
                Verdict = reading.Verdict,
                FiredRuleId = reading.FiredRuleId,
                CandidateId = candidateId,
                ObservedAt = snapshot.AsOf,
            };
            if (!await coverage.AddObservationUnscopedAsync(observation, ct))
            {
                continue;
            }

            recorded++;
            if (!snapshot.Verdicts.TryGetValue(ticker, out var days))
            {
                snapshot.Verdicts[ticker] = days = [];
            }

            days.Add(new CoverageVerdictDay(today, reading.Verdict));
        }

        return recorded;
    }

    /// <summary>
    /// The next earnings date for each idle, unprotected member (one batched read); a failed read marks the names
    /// "catalyst unknown" so the no-event rule holds off rather than removing a name on a missing answer.
    /// </summary>
    private async Task<CatalystLookup> NextEarningsAsync(CoverageSnapshot snapshot, DateTimeOffset asOf, CancellationToken ct)
    {
        var idle = snapshot.Members
            .Where(m => !snapshot.Held.Contains(m.Ticker) && !snapshot.Watchlist.Contains(m.Ticker)
                && !snapshot.HasThesis(m.Ticker))
            .Where(m => (LastEvent(snapshot, m) ?? m.AddedAt).AddDays(_options.NoEventDays) <= asOf)
            .Select(m => m.Ticker)
            .ToList();
        if (idle.Count == 0)
        {
            return CatalystLookup.None;
        }

        var today = DateOnly.FromDateTime(asOf.UtcDateTime);
        try
        {
            var events = await earnings.GetForTickersAsync(
                idle, today, today.AddDays(_options.CatalystLookaheadDays), EarningsEventType.Earnings, ct,
                surfaceProviderFailure: true);
            var next = events
                .GroupBy(e => e.Ticker.Trim().ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.Min(e => e.EventDate), StringComparer.OrdinalIgnoreCase);
            return new CatalystLookup(next, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Coverage: the earnings calendar could not be read; idle names are kept this run");
            return new CatalystLookup(new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase), false);
        }
    }

    private static CoverageTickerFacts MemberFacts(
        CoverageSnapshot snapshot, CoverageMember member, CatalystLookup catalysts)
    {
        var next = catalysts.Next.TryGetValue(member.Ticker, out var date) ? date : (DateOnly?)null;
        var facts = snapshot.Facts(member.Ticker, next, catalysts.Known, member.CorrelationToBook, 0);
        return facts with { LastEventAt = LastEvent(snapshot, member) };
    }

    private static DateTimeOffset? LastEvent(CoverageSnapshot snapshot, CoverageMember member)
    {
        var computed = snapshot.LastEvents.TryGetValue(member.Ticker, out var at) ? at : (DateTimeOffset?)null;
        if (computed is null)
        {
            return member.LastEventAt;
        }

        return member.LastEventAt is { } stored && stored > computed ? stored : computed;
    }

    /// <summary>Copies the fresh facts onto a continuing member; true when anything changed.</summary>
    private bool Refresh(CoverageMember member, CoverageTickerFacts facts, CoverageSnapshot snapshot)
    {
        var changed = false;
        var lastScore = snapshot.LatestScores.TryGetValue(member.Ticker, out var score) ? score.Id : (Guid?)null;
        var removeAfter = CoverageRules.RemoveAfter(facts, _options, member.AddedAt);

        changed |= Set(member.LastEventAt, facts.LastEventAt, v => member.LastEventAt = v);
        changed |= Set(member.LastScoreId, lastScore ?? member.LastScoreId, v => member.LastScoreId = v);
        changed |= Set(member.CorrelationToBook, facts.CorrelationToBook, v => member.CorrelationToBook = v);
        changed |= Set(member.RemoveAfter, removeAfter, v => member.RemoveAfter = v);
        return changed;
    }

    private static bool Set<T>(T current, T next, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(current, next))
        {
            return false;
        }

        assign(next);
        return true;
    }

    private CoverageMember NewMember(
        Guid userId, CoverageTickerFacts facts, CoverageAddDecision decision, CoverageSnapshot snapshot, DateTimeOffset asOf)
        => new()
        {
            UserId = userId,
            Ticker = facts.Ticker,
            AddedAt = asOf,
            AddedReason = decision.Reason,
            LastEventAt = facts.LastEventAt,
            LastScoreId = snapshot.LatestScores.TryGetValue(facts.Ticker, out var score) ? score.Id : null,
            CorrelationToBook = facts.CorrelationToBook,
            RemoveAfter = CoverageRules.RemoveAfter(facts, _options, asOf),
        };

    private static CoverageJournalEntry Entry(
        Guid userId, string ticker, CoverageAction action, string rule, CoverageReason reason, string detail,
        DateTimeOffset at, decimal? correlationToBook, int? rank)
        => new()
        {
            UserId = userId,
            Ticker = ticker,
            Action = action,
            Rule = rule,
            Reason = reason,
            Detail = detail,
            At = at,
            CorrelationToBook = correlationToBook,
            RankScore = rank,
        };

    private static string RankedDetail(CoverageAddDecision decision, CoverageTickerFacts facts, int rank)
        => string.Create(CultureInfo.InvariantCulture,
            $"{decision.Detail} Rank {rank} (scorecard {facts.RankBase?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} less correlation penalty {facts.CorrelationPenalty}).");

    private void AnnotateOverCap(List<CoverageJournalEntry> journal, List<CoverageMember> added)
    {
        var addedTickers = added.Select(m => m.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in journal.Where(e => e.Action == CoverageAction.Added && addedTickers.Contains(e.Ticker)
            && e.Rule is CoverageRuleIds.Held or CoverageRuleIds.Watchlist))
        {
            entry.Detail += string.Create(CultureInfo.InvariantCulture,
                $" Over the cap of {_options.MaxMembers}: held and watchlisted names always join.");
        }
    }

    /// <summary>The correlation to the book of <paramref name="ticker"/> and the D1 penalty it carries, read once per run.</summary>
    private async Task<(decimal? Value, int Penalty)> CorrelationAsync(
        Dictionary<string, (decimal? Value, int Penalty)> cache,
        string ticker,
        IReadOnlyCollection<CorrelationReference> book,
        CancellationToken ct)
    {
        if (cache.TryGetValue(ticker, out var known))
        {
            return known;
        }

        (decimal? Value, int Penalty) result;
        try
        {
            var read = await correlation.GetAsync(
                ticker, book, _opportunity.CorrelationTopHoldings, _opportunity.CorrelationMinBookCoverage, ct);
            var fact = CorrelationFact.From(read, _opportunity);
            result = (fact.BookCorrelation, fact.AddPenalty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Coverage: the correlation of {Ticker} to the book could not be read", ticker);
            result = (null, 0);
        }

        cache[ticker] = result;
        return result;
    }

    private sealed record CatalystLookup(IReadOnlyDictionary<string, DateOnly> Next, bool Known)
    {
        public static CatalystLookup None { get; } =
            new(new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase), true);
    }
}
