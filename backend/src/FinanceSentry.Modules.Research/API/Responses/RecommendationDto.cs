namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain.Recommendations;

/// <summary>
/// One proposal as the Decisions queue, the asset dossier and Ledger's tools see it (#1069). Enums cross as their names.
/// <see cref="CreatedBy"/> is always Ledger; <see cref="Origin"/> says whether Ledger drafted it or the rule sweep did.
/// <see cref="OrderText"/> is display text for the owner to place at the broker - nothing sends it.
/// <see cref="ExcessReturnPct"/>: the name's return since the decision minus the S&amp;P 500's, null until scored.
/// <see cref="DecisionValuePct"/>: what the owner's decision earned over the S&amp;P 500, signed by its direction
/// (<see cref="RecommendationValue.DecisionValuePct"/>, the one definition the Decisions page reads).
/// </summary>
public sealed record RecommendationDto(
    Guid Id,
    string Ticker,
    string Action,
    string Status,
    string CreatedBy,
    string Origin,
    string TriggerKind,
    string TriggerRef,
    string Rationale,
    string Confidence,
    string InvalidationTrigger,
    IReadOnlyList<RecommendationEvidenceDto> Evidence,
    decimal? SizeUsd,
    decimal? SizeShares,
    decimal? ShareOfBookBeforePct,
    decimal? ShareOfBookAfterPct,
    decimal? LimitPrice,
    string? LimitCurrency,
    string? Venue,
    string? OrderText,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    string Execution,
    DateTimeOffset? ExecutedAt,
    decimal? SubjectReturnPct,
    decimal? BenchmarkReturnPct,
    decimal? ExcessReturnPct,
    decimal? DecisionValuePct,
    DateTimeOffset? OutcomeScoredAt)
{
    public const string LedgerAuthor = "Ledger";

    public static RecommendationDto From(Recommendation r) => new(
        r.Id,
        r.Ticker,
        r.Action.ToString(),
        r.Status.ToString(),
        LedgerAuthor,
        r.Origin.ToString(),
        r.TriggerKind.ToString(),
        r.TriggerRef,
        r.Rationale,
        r.Confidence.ToString(),
        r.InvalidationTrigger,
        r.Evidence.Select(e => new RecommendationEvidenceDto(e.Kind, e.Label, e.RefId)).ToList(),
        r.SizeUsd,
        r.SizeShares,
        r.ShareOfBookBeforePct,
        r.ShareOfBookAfterPct,
        r.LimitPrice,
        r.LimitCurrency,
        r.Venue,
        r.OrderText,
        r.CreatedAt,
        r.ExpiresAt,
        r.DecidedAt,
        r.DecisionNote,
        r.Execution.ToString(),
        r.ExecutedAt,
        r.SubjectReturnPct,
        r.BenchmarkReturnPct,
        RecommendationValue.ExcessReturnPct(r),
        RecommendationValue.DecisionValuePct(r),
        r.OutcomeScoredAt);
}

public sealed record RecommendationEvidenceDto(string Kind, string Label, string? RefId);

/// <summary>The Decisions queue: pending proposals oldest first, and those closed in the last 30 days newest first.</summary>
public sealed record RecommendationsResult(
    IReadOnlyList<RecommendationDto> Pending,
    IReadOnlyList<RecommendationDto> Decided);
