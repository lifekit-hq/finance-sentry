namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// The coverage disposition digest (discovery funnel stage 4): every deterministic hit on a covered name since
/// <paramref name="Since"/> and what the pipeline did about it, plus the coverage adds and removes in the same span. The
/// pulse reads this one list instead of being woken per event. <paramref name="Since"/> defaults to a day before now.
/// </summary>
public sealed record ListCoverageEventsQuery(Guid UserId, DateTimeOffset? Since = null, int? Limit = null)
    : IQuery<CoverageEventDigest>;

/// <summary>One hit on a covered name and its disposition.</summary>
/// <param name="Disposition"><c>Kept</c>, <c>Removed</c> (a remove rule fired) or <c>Pending</c> (recorded, review not finished).</param>
/// <param name="Rescored">True when the review re-ran the candidate score (the <c>score_candidate</c> path, no model).</param>
/// <param name="ReviewNotice">The review-due notice the hit raised (<c>CandidateReviewDue</c>, <c>ThesisReviewDue</c>); null when it maps to neither.</param>
/// <param name="Rule">The remove rule that fired; null when the name was kept.</param>
public sealed record CoverageEventView(
    DateTimeOffset At,
    string Ticker,
    string Source,
    string Reference,
    string Summary,
    string Disposition,
    bool Rescored,
    string? ReviewNotice,
    string? Verdict,
    string? Rule,
    string Detail,
    bool Held,
    bool Watchlisted,
    bool HasThesis);

public sealed record CoverageEventDigest(
    DateTimeOffset Since,
    DateTimeOffset Until,
    int Total,
    int Removed,
    int Rescored,
    bool Truncated,
    IReadOnlyList<CoverageEventView> Events,
    IReadOnlyList<CoverageJournalView> Changes);

public sealed class ListCoverageEventsQueryHandler(
    ICoverageRepository coverage,
    CoverageSnapshotReader snapshots,
    TimeProvider clock,
    IOptions<CoverageOptions> options) : IQueryHandler<ListCoverageEventsQuery, CoverageEventDigest>
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(1);

    public async Task<CoverageEventDigest> Handle(ListCoverageEventsQuery query, CancellationToken ct)
    {
        var config = options.Value;
        var now = clock.GetUtcNow();
        var since = query.Since ?? now - DefaultWindow;
        var take = Math.Clamp(query.Limit ?? config.MaxDigestEvents, 1, config.MaxDigestEvents);

        var (events, total) = await coverage.ListEventsUnscopedAsync(query.UserId, since, take, ct);
        var snapshot = await snapshots.ReadAsync(query.UserId, now, ct, flagsOnly: true);
        var journal = await coverage.ListJournalUnscopedAsync(query.UserId, null, config.MaxDigestEvents, ct);

        return new CoverageEventDigest(
            since,
            now,
            total,
            Removed: events.Count(e => e.Disposition == CoverageEventDisposition.Removed),
            Rescored: events.Count(e => e.Rescored),
            Truncated: total > events.Count,
            [.. events.Select(e => new CoverageEventView(
                e.OccurredAt, e.Ticker, e.Source, e.Reference, e.Summary, e.Disposition.ToString(), e.Rescored,
                e.ReviewNotice, e.Verdict, e.Rule, e.Detail,
                snapshot.Held.Contains(e.Ticker), snapshot.Watchlist.Contains(e.Ticker), snapshot.HasThesis(e.Ticker)))],
            [.. journal.Where(j => j.At >= since).Select(CoverageJournalViews.From)]);
    }
}
