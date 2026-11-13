namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using System.Globalization;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Explains one ticker's place in the coverage universe from the same snapshot and the same
/// <see cref="CoverageRules"/> the evaluation uses, so the answer cannot drift from what the job does. Read-only:
/// it records nothing and calls no model.
/// </summary>
public sealed class CoverageExplainer(
    CoverageSnapshotReader snapshots,
    ICoverageRepository coverage,
    IReturnCorrelationReader correlation,
    IEarningsCalendarService earnings,
    IOptions<CoverageOptions> options,
    IOptions<OpportunityOptions> opportunityOptions,
    ILogger<CoverageExplainer> logger)
{
    private const int JournalRows = 20;

    private readonly CoverageOptions _options = options.Value;
    private readonly OpportunityOptions _opportunity = opportunityOptions.Value;

    public async Task<CoverageExplanation> ExplainAsync(
        Guid userId, string ticker, DateTimeOffset asOf, CancellationToken ct = default)
    {
        ticker = CoverageSnapshotReader.Normalize(ticker);
        var snapshot = await snapshots.ReadAsync(userId, asOf, ct);
        var member = snapshot.Members.FirstOrDefault(m => string.Equals(m.Ticker, ticker, StringComparison.OrdinalIgnoreCase));

        var (nextEarnings, known) = await NextEarningsAsync(ticker, asOf, ct);
        var (corr, penalty) = await CorrelationAsync(snapshot, ticker, ct);
        var facts = snapshot.Facts(ticker, nextEarnings, known, corr, penalty);
        if (member is not null)
        {
            facts = facts with { LastEventAt = LatestOf(facts.LastEventAt, member.LastEventAt) };
        }

        var memberSince = member?.AddedAt ?? asOf;
        var journal = await coverage.ListJournalUnscopedAsync(userId, ticker, JournalRows, ct);
        var addRule = journal.FirstOrDefault(e => e.Action == CoverageAction.Added)?.Rule;

        var addRules = AddRuleViews(facts, asOf);
        var removeRules = RemoveRuleViews(facts, asOf, memberSince);
        var rank = facts.ActiveCandidate is null ? (int?)null : CoverageRules.Rank(facts);
        var cap = new CoverageCapView(_options.MaxMembers, snapshot.Members.Count, snapshot.Members.Count >= _options.MaxMembers);

        return new CoverageExplanation(
            ticker,
            member is not null,
            Summary(member, facts, addRules, removeRules, cap, asOf, memberSince),
            member is null ? null : View(member, snapshot, addRule),
            facts.HasActiveThesis,
            rank,
            facts.RankBase,
            penalty,
            addRules,
            removeRules,
            cap,
            facts.Verdicts,
            [.. journal.Select(CoverageJournalViews.From)]);
    }

    public static CoverageMemberView View(CoverageMember member, CoverageSnapshot snapshot, string? addedRule)
        => new(
            member.Ticker, member.AddedAt, member.AddedReason.ToString(), addedRule,
            snapshot.Held.Contains(member.Ticker), snapshot.Watchlist.Contains(member.Ticker),
            snapshot.HasThesis(member.Ticker), member.LastEventAt, member.LastScoreId, member.CorrelationToBook,
            member.RemoveAfter);

    private IReadOnlyList<CoverageRuleView> AddRuleViews(CoverageTickerFacts facts, DateTimeOffset asOf)
    {
        var active = facts.ActiveCandidate;
        var buyDays = CoverageRules.BuyDaysInWindow(facts, _options, asOf);
        var nominated = active is { Source: CandidateSource.Ledger };
        var nominationStands = active is not null && CoverageRules.NominationStands(facts, active);

        return
        [
            new(CoverageRuleIds.Held, "The user holds the ticker: always covered.", true, facts.Held,
                facts.Held ? "Held in the book." : "Not held."),
            new(CoverageRuleIds.Watchlist, "The ticker is on the watchlist: always covered.", true, facts.Watchlisted,
                facts.Watchlisted ? "On the watchlist." : "Not on the watchlist."),
            new(CoverageRuleIds.LedgerNomination, "Ledger nominated the name with a decision note.", true,
                nominationStands,
                active is null ? "No Active candidate."
                    : !nominated ? $"The Active candidate is a {active.Source} candidate, not a Ledger nomination."
                    : active.HasNominationNote && !nominationStands ? string.Create(CultureInfo.InvariantCulture, $"The Ledger nomination predates the name's removal ({facts.LastRemovedAt:yyyy-MM-dd}); it rejoins only on a fresh nomination.")
                    : active.HasNominationNote ? "An Active Ledger candidate with a decision note."
                    : "An Active Ledger candidate, but its creation carries no decision note."),
            new(CoverageRuleIds.BuyTwice,
                string.Create(CultureInfo.InvariantCulture,
                    $"An Active Scan or Ledger candidate reads Buy or StrongBuy on {_options.BuyObservationsRequired} days within {_options.BuyWindowDays} days."),
                true,
                active is { Source: CandidateSource.Scan or CandidateSource.Ledger } && buyDays >= _options.BuyObservationsRequired,
                active is null
                    ? "No Active candidate."
                    : active.Source == CandidateSource.User ? "The Active candidate is a User candidate; only Scan and Ledger candidates qualify."
                    : string.Create(CultureInfo.InvariantCulture,
                        $"{buyDays} of {_options.BuyObservationsRequired} Buy or StrongBuy days inside {_options.BuyWindowDays} days.")),
        ];
    }

    private IReadOnlyList<CoverageRuleView> RemoveRuleViews(CoverageTickerFacts facts, DateTimeOffset asOf, DateTimeOffset memberSince)
    {
        var applies = !facts.Protected;
        var protection = facts.Held ? "held"
            : facts.Watchlisted ? "on the watchlist"
            : facts.HasActiveThesis ? "carrying an active thesis"
            : null;
        var streak = CoverageRules.AvoidStreak(facts);
        var expired = CoverageRules.CandidateExpired(facts);
        var idleSince = CoverageRules.IdleSince(facts, memberSince);

        string Guarded(string detail) => protection is null ? detail : $"The name is {protection}, so this rule cannot remove it. {detail}";

        return
        [
            new(CoverageRuleIds.AvoidTwice,
                string.Create(CultureInfo.InvariantCulture, $"The last {_options.AvoidObservationsRequired} verdict readings are Avoid."),
                applies, streak >= _options.AvoidObservationsRequired,
                Guarded(string.Create(CultureInfo.InvariantCulture,
                    $"{streak} consecutive Avoid reading(s); {_options.AvoidObservationsRequired} remove."))),
            new(CoverageRuleIds.CandidateExpired, "The name's candidate expired.", applies, expired is not null,
                Guarded(expired is null ? "The newest candidate has not expired (or the name has no candidate)."
                    : string.Create(CultureInfo.InvariantCulture, $"The candidate created {expired.CreatedAt:yyyy-MM-dd} expired."))),
            new(CoverageRuleIds.NoEvent,
                string.Create(CultureInfo.InvariantCulture,
                    $"No event for {_options.NoEventDays} days and no dated catalyst in the next {_options.CatalystLookaheadDays} days."),
                applies, CoverageRules.NoEventDue(facts, _options, asOf, memberSince),
                Guarded(string.Create(CultureInfo.InvariantCulture,
                    $"Last event {idleSince:yyyy-MM-dd}; next dated catalyst {(facts.NextCatalyst is { } c ? c.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : facts.CatalystKnown ? "none" : "unknown (earnings lookup failed)")}."))),
        ];
    }

    private string Summary(
        CoverageMember? member,
        CoverageTickerFacts facts,
        IReadOnlyList<CoverageRuleView> addRules,
        IReadOnlyList<CoverageRuleView> removeRules,
        CoverageCapView cap,
        DateTimeOffset asOf,
        DateTimeOffset memberSince)
    {
        if (member is not null)
        {
            var fired = CoverageRules.EvaluateRemove(facts, _options, asOf, memberSince);
            var tail = fired is not null
                ? $" The next evaluation removes it: {fired.Rule} ({fired.Detail})"
                : facts.Protected ? " It cannot be removed while it is held, watchlisted or carries an active thesis."
                : (member.RemoveAfter ?? CoverageRules.RemoveAfter(facts, _options, memberSince)) is { } after ? $" It is removed after {after:yyyy-MM-dd} if nothing happens first." : string.Empty;
            return string.Create(CultureInfo.InvariantCulture,
                $"Covered since {member.AddedAt:yyyy-MM-dd} ({member.AddedReason}).{tail}");
        }

        var met = addRules.FirstOrDefault(r => r.Met);
        if (met is null)
        {
            return "Not covered: no add rule is met (held, watchlist, Ledger nomination with a decision note, or Buy twice inside the window).";
        }

        return CoverageRules.EvaluateAdd(facts, _options, asOf) is null
            ? $"Not covered: {met.Rule} is met, but a remove rule already matches the same facts, so the name would be removed straight away."
            : met.Rule is CoverageRuleIds.Held or CoverageRuleIds.Watchlist
                ? $"Not yet covered: {met.Rule} is met and the next evaluation adds it."
                : cap.Full
                    ? $"Not covered: {met.Rule} is met but the cap of {cap.MaxMembers} is full; it joins when a slot frees and it ranks highest."
                    : $"Not yet covered: {met.Rule} is met and the next evaluation adds it, ranked against the other qualifying names if the cap binds.";
    }

    private static DateTimeOffset? LatestOf(DateTimeOffset? a, DateTimeOffset? b)
        => a is null ? b : b is null ? a : a > b ? a : b;

    private async Task<(DateOnly? Next, bool Known)> NextEarningsAsync(string ticker, DateTimeOffset asOf, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(asOf.UtcDateTime);
        try
        {
            var events = await earnings.GetForTickersAsync(
                [ticker], today, today.AddDays(_options.CatalystLookaheadDays), EarningsEventType.Earnings, ct,
                surfaceProviderFailure: true);
            return (events.Count == 0 ? null : events.Min(e => e.EventDate), true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Coverage explain: the earnings calendar for {Ticker} could not be read", ticker);
            return (null, false);
        }
    }

    private async Task<(decimal? Value, int Penalty)> CorrelationAsync(CoverageSnapshot snapshot, string ticker, CancellationToken ct)
    {
        try
        {
            var book = snapshot.Holdings.Select(h => new CorrelationReference(h.Symbol, h.UsdValue)).ToList();
            var read = await correlation.GetAsync(
                ticker, book, _opportunity.CorrelationTopHoldings, _opportunity.CorrelationMinBookCoverage, ct);
            var fact = CorrelationFact.From(read, _opportunity);
            return (fact.BookCorrelation, fact.AddPenalty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Coverage explain: the correlation of {Ticker} to the book could not be read", ticker);
            return (null, 0);
        }
    }
}
