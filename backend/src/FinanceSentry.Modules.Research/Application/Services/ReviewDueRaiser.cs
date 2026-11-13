namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Turns a deterministic hit on a ticker (a filing, a passed date, a news cluster) into the "take a fresh look" notice for
/// every thesis and Active candidate that name belongs to (the proactive thesis-maintenance loop, step 1). Pure mapping and
/// a single alert call per subject: whether anything actually changed is Ledger's call, never this class's, and no model is
/// consulted. Once-only is the alert generator's job (<see cref="IAlertGeneratorService.GenerateReviewDueAlertAsync"/>
/// keys on subject, source and reference).
/// </summary>
public sealed class ReviewDueRaiser(
    IThesisRepository theses,
    ICandidateRepository candidates,
    IAlertGeneratorService alerts)
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
}

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

    /// <summary>The subjects a hit on <paramref name="ticker"/> raises a notice for; empty for any other name.</summary>
    public IReadOnlyList<ReviewSubject> For(string ticker)
        => _byTicker.TryGetValue(ticker.Trim(), out var subjects) ? subjects : [];

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
