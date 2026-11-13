namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>Reviews the hits a detector noted on covered names (<see cref="ReviewSubjects.CoverageHits"/>).</summary>
public interface ICoverageEventReviewer
{
    /// <summary>Records, re-scores and rule-checks the covered-name hits noted on <paramref name="subjects"/>.</summary>
    Task ReviewAsync(Guid userId, ReviewSubjects subjects, CancellationToken ct = default);
}

/// <summary>
/// Events on coverage names (discovery funnel stage 4): the deterministic "something happened to a name we cover" handler.
/// For each hit a detector noted on a coverage member, once ever per (ticker, source, reference):
/// <list type="number">
/// <item>records it as a <see cref="CoverageEvent"/> (the digest's rows) and moves the member's <c>LastEventAt</c> to now;</item>
/// <item>re-scores the member's Active candidate (<see cref="ScoreCandidateCommand"/>, the <c>score_candidate</c> path: market structure and fundamentals read, no model);</item>
/// <item>runs the stage 1 remove rules on the member (<see cref="CoverageEvaluator.ReviewTickersAsync"/>, which reads <see cref="CoverageRules"/>, so a removal journals the rule that fired and the removal cutoff applies on the way back in);</item>
/// <item>writes the outcome back on the event row.</item>
/// </list>
/// The review-due notice (<c>CandidateReviewDue</c> / <c>ThesisReviewDue</c>) is raised by the detector through
/// <see cref="ReviewDueRaiser.RaiseAsync"/>, once per subject, source and reference; this class only reports which one the hit
/// maps to. A repeat of a recorded hit returns before touching anything, so a detector that re-reads the same filing or
/// the same day's cluster every run costs one lookup. No model is called anywhere here.
/// </summary>
public sealed class CoverageEventReviewer(
    ICoverageRepository coverage,
    ICommandHandler<ScoreCandidateCommand, ScoreCandidateResult> scorer,
    CoverageEvaluator evaluator,
    TimeProvider clock,
    ILogger<CoverageEventReviewer> logger) : ICoverageEventReviewer
{
    private const int MaxSummaryLength = 500;
    private const int MaxDetailLength = 1000;
    private const int MaxReferenceLength = 200;

    public async Task ReviewAsync(Guid userId, ReviewSubjects subjects, CancellationToken ct = default)
    {
        if (subjects.CoverageHits.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var members = (await coverage.ListMembersUnscopedAsync(userId, ct))
            .Select(m => m.Ticker.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var candidates = subjects.CoverageHits
            .Where(h => members.Contains(h.Ticker))
            .Select(h => new CoverageEvent
            {
                UserId = userId,
                Ticker = h.Ticker,
                Source = h.Source,
                Reference = Truncate(h.Reference, MaxReferenceLength),
                Summary = Truncate(h.What, MaxSummaryLength),
                OccurredAt = now,
                ReviewNotice = NoticeFor(subjects, h.Ticker),
            })
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var fresh = await coverage.AddEventsUnscopedAsync(userId, candidates, ct);
        if (fresh.Count == 0)
        {
            return;
        }

        var tickers = fresh.Select(e => e.Ticker).Distinct(StringComparer.Ordinal).ToList();
        await coverage.TouchMembersUnscopedAsync(userId, tickers.ToDictionary(t => t, _ => now, StringComparer.Ordinal), ct);

        var rescored = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ticker in tickers)
        {
            rescored[ticker] = await RescoreAsync(userId, subjects, ticker, ct);
        }

        IReadOnlyDictionary<string, CoverageTickerReview> reviews;
        try
        {
            reviews = await evaluator.ReviewTickersAsync(userId, tickers, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The event stays Pending (recorded, never reviewed); the nightly evaluation still applies the rules.
            logger.LogError(ex, "Coverage event review failed for user {UserId}; the nightly evaluation will apply the rules", userId);
            return;
        }

        foreach (var coverageEvent in fresh)
        {
            coverageEvent.ReviewedAt = now;
            coverageEvent.Rescored = rescored[coverageEvent.Ticker].Length == 0;
            if (reviews.TryGetValue(coverageEvent.Ticker, out var review))
            {
                coverageEvent.Disposition = review.Removed ? CoverageEventDisposition.Removed : CoverageEventDisposition.Kept;
                coverageEvent.Verdict = review.Verdict;
                coverageEvent.Rule = review.Rule;
                coverageEvent.Detail = Truncate(
                    string.Join(" ", new[] { review.Detail, rescored[coverageEvent.Ticker] }.Where(d => d.Length > 0)),
                    MaxDetailLength);
            }
            else
            {
                coverageEvent.Disposition = CoverageEventDisposition.Kept;
                coverageEvent.Detail = Truncate(rescored[coverageEvent.Ticker], MaxDetailLength);
            }
        }

        await coverage.UpdateEventsUnscopedAsync(fresh, ct);

        logger.LogInformation(
            "Coverage events for user {UserId}: {Events} new, {Removed} name(s) removed",
            userId, fresh.Count, reviews.Values.Count(r => r.Removed));
    }

    /// <summary>Re-scores the ticker's Active candidate; returns an empty string on success or why it did not run.</summary>
    private async Task<string> RescoreAsync(Guid userId, ReviewSubjects subjects, string ticker, CancellationToken ct)
    {
        var candidate = subjects.For(ticker).FirstOrDefault(s => s.Kind == ReviewSubjectKind.Candidate);
        if (candidate is null)
        {
            return "No Active candidate to re-score.";
        }

        try
        {
            await scorer.Handle(new ScoreCandidateCommand(userId, ticker), ct);
            return string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Coverage event rescore failed for {Ticker}", ticker);
            return $"Re-score failed: {ex.GetType().Name}.";
        }
    }

    private static string? NoticeFor(ReviewSubjects subjects, string ticker)
    {
        var kinds = subjects.For(ticker)
            .Select(s => s.Kind == ReviewSubjectKind.Thesis ? "ThesisReviewDue" : "CandidateReviewDue")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return kinds.Count == 0 ? null : string.Join(',', kinds);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
