namespace FinanceSentry.Modules.Research.Domain.PolicyReviews;

/// <summary>
/// One completed scheduled review of the investment policy statement (#696): current positioning
/// measured against the statement's bands, the adjustments it suggests and why. Recommend-only —
/// a review records a proposal for the operator and never places, stages or routes an order.
/// </summary>
public class PolicyReview
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>The statement version the review measured against — the source of targets and bands.</summary>
    public Guid PolicyStatementId { get; set; }

    public int PolicyStatementVersion { get; set; }

    public string ReviewCadence { get; set; } = string.Empty;

    /// <summary>When the review was due on the recorded cadence.</summary>
    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>Whole days past <see cref="DueAt"/> the review opened; 0 when on time.</summary>
    public int DaysOverdue { get; set; }

    /// <summary>True when the review opened beyond the grace period, i.e. the scheduled one was missed.</summary>
    public bool WasMissed { get; set; }

    public decimal TotalValueUsd { get; set; }

    public List<PolicyReviewSleeve> Sleeves { get; set; } = [];

    public List<PolicyReviewAdjustment> Adjustments { get; set; } = [];

    public string Rationale { get; set; } = string.Empty;
}

/// <summary>One asset-class sleeve's drift against its policy band at review time.</summary>
public sealed record PolicyReviewSleeve(
    string AssetClass,
    decimal TargetPct,
    decimal MinPct,
    decimal MaxPct,
    decimal ActualPct,
    decimal ActualValueUsd,
    decimal DriftPct,
    string Status);

/// <summary>
/// A suggested adjustment — <see cref="PolicyReviewAdjustmentAction"/> plus an approximate size and
/// its rationale. A suggestion for the operator, never an order.
/// </summary>
public sealed record PolicyReviewAdjustment(
    string AssetClass,
    string Action,
    decimal ApproxAmountUsd,
    string Rationale);

public static class PolicyReviewAdjustmentAction
{
    /// <summary>Over its band: reduce toward target.</summary>
    public const string Trim = "Trim";

    /// <summary>Under its band: increase toward target.</summary>
    public const string Add = "Add";

    /// <summary>Held without a target in the statement: decide whether the policy or the holding changes.</summary>
    public const string Review = "Review";
}
