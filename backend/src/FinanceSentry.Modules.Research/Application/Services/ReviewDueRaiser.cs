namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Turns a deterministic hit on a ticker (a filing, a passed date, a news cluster) into the "take a fresh look" notice for
/// every thesis and Active candidate that name belongs to (the proactive thesis-maintenance loop, step 1). Pure mapping and
/// a single alert call per subject: whether anything actually changed is Ledger's call, never this class's, and no model is
/// consulted. Once-only is the alert generator's job (<see cref="IAlertGeneratorService.GenerateReviewDueAlertAsync"/>
/// keys on subject, source and reference).
/// <para>
/// A hit on a covered name (a job that opted in through <see cref="ReviewSubjects.TrackCoverage"/>) is also noted on the
/// subjects, even when the name maps to no thesis or candidate, and <see cref="CompleteAsync"/> hands the noted hits to the
/// coverage event review once the user's pass is over.
/// </para>
/// </summary>
public sealed class ReviewDueRaiser(
    IThesisRepository theses,
    ICandidateRepository candidates,
    IAlertGeneratorService alerts,
    ICoverageEventReviewer coverageReview)
{
    /// <summary>Loads the user's subjects once, so a detector pass over many tickers reads them once.</summary>
    public async Task<ReviewSubjects> LoadSubjectsAsync(Guid userId, CancellationToken ct = default)
    {
        var thesisList = await theses.ListUnscopedAsync(userId, ct);
        var activeCandidates = (await candidates.ListAllUnscopedAsync(userId, ct))
            .Where(c => c.Status == CandidateStatus.Active)
            .ToList();
        return new ReviewSubjects(thesisList, activeCandidates);
    }

    /// <summary>
    /// Raises one notice per subject the <paramref name="hitTicker"/> maps to. <paramref name="reference"/> names the event
    /// behind the hit and must be stable across re-detection; fold the hit ticker into it where two tickers of one subject
    /// can hit on the same day. Returns the number of subjects notified (before the generator's once-only dedup).
    /// </summary>
    public async Task<int> RaiseAsync(
        Guid userId,
        ReviewSubjects subjects,
        string hitTicker,
        string source,
        string reference,
        IReadOnlyList<string> evidence,
        string what,
        CancellationToken ct = default)
    {
        subjects.NoteCoverageHit(hitTicker, source, reference, what, subjects.NoticeFor(hitTicker));

        var raised = 0;
        foreach (var subject in subjects.For(hitTicker))
        {
            var headline = string.Equals(subject.Ticker, hitTicker, StringComparison.OrdinalIgnoreCase)
                ? $"{subject.Ticker}: {what}"
                : $"{subject.Ticker}: {what} (on {hitTicker.ToUpperInvariant()}, a trigger proxy)";
            await alerts.GenerateReviewDueAlertAsync(
                userId,
                new ReviewDue(subject.Kind, subject.Id, subject.Ticker, source, reference, evidence, headline),
                ct);
            raised++;
        }

        return raised;
    }

    /// <summary>
    /// Notes a hit on a covered name without raising a review notice: for a signal that is an event for coverage (an
    /// earnings date a few days out) but is no reason to ask Ledger for a fresh look at a thesis.
    /// </summary>
    public void NoteCoverageOnly(ReviewSubjects subjects, string hitTicker, string source, string reference, string what)
        => subjects.NoteCoverageHit(hitTicker, source, reference, what, null);

    /// <summary>
    /// Ends a user's detector pass: the covered-name hits noted along the way are recorded once, move the members'
    /// <c>LastEventAt</c>, re-score the Active candidates behind them and run the coverage remove rules (no model call).
    /// A no-op when nothing was noted.
    /// </summary>
    public async Task CompleteAsync(Guid userId, ReviewSubjects subjects, CancellationToken ct = default)
    {
        if (subjects.CoverageHits.Count == 0)
        {
            return;
        }

        await coverageReview.ReviewAsync(userId, subjects, ct);
    }
}

/// <summary>A deterministic hit on a covered name, as a detector noted it; <paramref name="Notice"/> is the review-due notice the detector raised for it, null when it raised none.</summary>
public sealed record CoverageHit(string Ticker, string Source, string Reference, string What, string? Notice = null);

/// <summary>A thesis or an Active candidate that a ticker hit maps to.</summary>
public sealed record ReviewSubject(ReviewSubjectKind Kind, Guid Id, string Ticker);

/// <summary>
/// The names that have something to review, and which subject each maps to. A thesis maps from its own ticker and from every
/// invalidation-trigger proxy; an Active candidate maps from its ticker unless a thesis already sits on that same ticker (the
/// thesis covers the name). A name with neither maps to nothing, so a held name with no thesis and no candidate raises no
/// review notice.
/// </summary>
public sealed class ReviewSubjects
{
    private readonly Dictionary<string, List<ReviewSubject>> _byTicker = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _coverage = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CoverageHit> _hits = [];

    public ReviewSubjects(IEnumerable<InvestmentThesis> theses, IEnumerable<OpportunityCandidate> activeCandidates)
    {
        var thesisOwnTickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var thesis in theses)
        {
            if (string.IsNullOrWhiteSpace(thesis.Ticker))
            {
                continue;
            }

            thesisOwnTickers.Add(thesis.Ticker);
            var subject = new ReviewSubject(ReviewSubjectKind.Thesis, thesis.Id, thesis.Ticker);
            Add(thesis.Ticker, subject);
            foreach (var proxy in thesis.InvalidationTriggers
                         .Select(t => t.ProxyTicker)
                         .Where(p => !string.IsNullOrWhiteSpace(p))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Add(proxy!, subject);
            }
        }

        foreach (var candidate in activeCandidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.Ticker) && !thesisOwnTickers.Contains(candidate.Ticker))
            {
                Add(candidate.Ticker, new ReviewSubject(ReviewSubjectKind.Candidate, candidate.Id, candidate.Ticker));
            }
        }
    }

    /// <summary>Every ticker whose hit raises a notice: thesis tickers, trigger proxies and Active candidate tickers.</summary>
    public IReadOnlyCollection<string> Tickers => _byTicker.Keys;

    /// <summary>The covered names this pass watches (coverage members that are not crypto), empty until a job opts in.</summary>
    public IReadOnlyCollection<string> CoverageTickers => _coverage;

    /// <summary>Every ticker a detector should read: the notice tickers and the covered names.</summary>
    public IReadOnlyCollection<string> WatchedTickers => [.. _byTicker.Keys.Concat(_coverage).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The covered-name hits noted so far, once each, in the order they were found.</summary>
    public IReadOnlyList<CoverageHit> CoverageHits => _hits;

    /// <summary>Opts the pass into covered names: hits on them are noted for the coverage event review.</summary>
    public void TrackCoverage(IEnumerable<string> tickers)
    {
        foreach (var ticker in tickers.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            _coverage.Add(ticker.Trim());
        }
    }

    internal void NoteCoverageHit(string ticker, string source, string reference, string what, string? notice)
    {
        if (!_coverage.Contains(ticker.Trim()))
        {
            return;
        }

        var hit = new CoverageHit(ticker.Trim().ToUpperInvariant(), source, reference, what, notice);
        if (!_hits.Any(h => h.Ticker == hit.Ticker && h.Source == hit.Source && h.Reference == hit.Reference))
        {
            _hits.Add(hit);
        }
    }

    /// <summary>The subjects a hit on <paramref name="ticker"/> raises a notice for; empty for any other name.</summary>
    public IReadOnlyList<ReviewSubject> For(string ticker)
        => _byTicker.TryGetValue(ticker.Trim(), out var subjects) ? subjects : [];

    internal string? NoticeFor(string ticker)
    {
        var kinds = For(ticker)
            .Select(s => s.Kind == ReviewSubjectKind.Thesis ? "ThesisReviewDue" : "CandidateReviewDue")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return kinds.Count == 0 ? null : string.Join(',', kinds);
    }

    private void Add(string ticker, ReviewSubject subject)
    {
        var key = ticker.Trim();
        if (!_byTicker.TryGetValue(key, out var list))
        {
            list = [];
            _byTicker[key] = list;
        }

        if (!list.Contains(subject))
        {
            list.Add(subject);
        }
    }
}
