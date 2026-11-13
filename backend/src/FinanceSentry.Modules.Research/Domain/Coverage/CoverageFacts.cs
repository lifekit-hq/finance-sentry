namespace FinanceSentry.Modules.Research.Domain.Coverage;

using FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>A candidate of the ticker, as the coverage rules read it.</summary>
/// <param name="HasNominationNote">True when the candidate's creation event carries a decision note (a Ledger nomination is only a nomination with one).</param>
public sealed record CoverageCandidateFacts(
    Guid CandidateId,
    CandidateSource Source,
    CandidateStatus Status,
    DateTimeOffset CreatedAt,
    bool HasNominationNote);

/// <summary>One stored verdict reading of the ticker.</summary>
public sealed record CoverageVerdictDay(DateOnly Day, string Verdict);

/// <summary>
/// Everything the coverage rules read about one ticker, gathered once per evaluation. Pure data: the rules
/// make no read of their own, so the same facts always give the same decision.
/// </summary>
/// <param name="Candidates">Every candidate of the ticker in any status.</param>
/// <param name="Verdicts">Stored verdict readings, ascending by day.</param>
/// <param name="LastEventAt">The latest dated event for the ticker; null when none is known.</param>
/// <param name="NextCatalyst">The next dated catalyst inside the lookahead; null when none is known.</param>
/// <param name="CatalystKnown">False when the catalyst lookup failed, so "no catalyst" cannot be told from "unknown".</param>
/// <param name="RankBase">Mean of the latest candidate score's structure and fundamentals sub-scores (0-100); null when unscored.</param>
/// <param name="CorrelationToBook">63-day correlation to the book; null when not evaluable.</param>
/// <param name="CorrelationPenalty">Points the D1 add-rule penalty takes off the rank (0 when the name does not move with the book).</param>
public sealed record CoverageTickerFacts(
    string Ticker,
    bool Held,
    bool Watchlisted,
    bool HasActiveThesis,
    IReadOnlyList<CoverageCandidateFacts> Candidates,
    IReadOnlyList<CoverageVerdictDay> Verdicts,
    DateTimeOffset? LastEventAt,
    DateOnly? NextCatalyst,
    bool CatalystKnown,
    int? RankBase,
    decimal? CorrelationToBook,
    int CorrelationPenalty)
{
    /// <summary>Held, watchlisted or carrying an active thesis: the remove rules cannot touch the name.</summary>
    public bool Protected => Held || Watchlisted || HasActiveThesis;

    public CoverageCandidateFacts? ActiveCandidate =>
        Candidates.FirstOrDefault(c => c.Status == CandidateStatus.Active);

    /// <summary>The newest candidate, any status.</summary>
    public CoverageCandidateFacts? LatestCandidate =>
        Candidates.OrderByDescending(c => c.CreatedAt).FirstOrDefault();
}

/// <summary>A rule's verdict on a ticker: which one fired and the facts it read.</summary>
public sealed record CoverageAddDecision(string Rule, CoverageReason Reason, bool Pinned, string Detail);

public sealed record CoverageRemoveDecision(string Rule, string Detail);
