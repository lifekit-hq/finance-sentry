namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain.PolicyReviews;

/// <summary>
/// Where the scheduled policy review stands (#696): the recorded cadence, when it was last reviewed,
/// when the next review is due and whether it is overdue or missed — plus the latest review's proposal.
/// </summary>
public sealed record PolicyReviewStatusDto(
    bool HasIps,
    string? Cadence,
    bool CadenceRecognised,
    DateTimeOffset? LastReviewedAt,
    DateTimeOffset? NextDueAt,
    bool IsDue,
    bool IsMissed,
    int DaysOverdue,
    PolicyReviewDto? LatestReview);

public sealed record PolicyReviewDto(
    Guid Id,
    int PolicyStatementVersion,
    string ReviewCadence,
    DateTimeOffset DueAt,
    DateTimeOffset CompletedAt,
    int DaysOverdue,
    bool WasMissed,
    decimal TotalValueUsd,
    IReadOnlyList<PolicyReviewSleeve> Sleeves,
    IReadOnlyList<PolicyReviewAdjustment> Adjustments,
    string Rationale);
