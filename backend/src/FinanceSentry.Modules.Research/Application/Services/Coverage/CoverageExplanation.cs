namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using FinanceSentry.Modules.Research.Domain.Coverage;

/// <summary>One coverage rule against one ticker: whether it is met now and the facts it read.</summary>
/// <param name="Applies">False when the rule cannot act on the name (a remove rule on a held, watchlisted or thesis name).</param>
public sealed record CoverageRuleView(string Rule, string Description, bool Applies, bool Met, string Detail);

public sealed record CoverageMemberView(
    string Ticker,
    DateTimeOffset AddedAt,
    string AddedReason,
    string? AddedRule,
    bool Held,
    bool Watchlisted,
    bool HasThesis,
    DateTimeOffset? LastEventAt,
    Guid? LastScoreId,
    decimal? CorrelationToBook,
    DateTimeOffset? RemoveAfter)
{
    /// <summary>When the newest stage 6 research memo for the name was written; null when it has none.</summary>
    public DateTimeOffset? LatestMemoAt { get; init; }
}

/// <summary>
/// A stage 6 research memo as read back: the structured content when the reply parsed, the raw text when it did not.
/// Logged only; <see cref="PaperOnly"/> is always true (nothing acts on a memo until the paper record grades it).
/// </summary>
public sealed record CoverageMemoView(
    string Ticker,
    DateTimeOffset At,
    string Status,
    string? Verdict,
    string ModelId,
    decimal CostUsd,
    CoverageMemoContent? Content,
    string? RawText)
{
    public bool PaperOnly => true;
}

public sealed record CoverageJournalView(
    DateTimeOffset At,
    string Ticker,
    string Action,
    string Rule,
    string Reason,
    string Detail,
    decimal? CorrelationToBook,
    int? RankScore);

public sealed record CoverageCapView(int MaxMembers, int Members, bool Full);

/// <summary>
/// Why a ticker is or is not in the coverage universe: the summary sentence, the state of every add and remove rule
/// against it, the cap, the stored verdict readings and the journal of its adds and removes.
/// </summary>
public sealed record CoverageExplanation(
    string Ticker,
    bool IsMember,
    string Summary,
    CoverageMemberView? Member,
    bool HasThesis,
    int? Rank,
    int? RankBase,
    int CorrelationPenalty,
    IReadOnlyList<CoverageRuleView> AddRules,
    IReadOnlyList<CoverageRuleView> RemoveRules,
    CoverageCapView Cap,
    IReadOnlyList<CoverageVerdictDay> RecentVerdicts,
    IReadOnlyList<CoverageJournalView> Journal)
{
    /// <summary>The newest research memo for the name (stage 6); null when none was written.</summary>
    public CoverageMemoView? Memo { get; init; }
}

public static class CoverageJournalViews
{
    public static CoverageJournalView From(CoverageJournalEntry e) => new(
        e.At, e.Ticker, e.Action.ToString(), e.Rule, e.Reason.ToString(), e.Detail, e.CorrelationToBook, e.RankScore);
}
