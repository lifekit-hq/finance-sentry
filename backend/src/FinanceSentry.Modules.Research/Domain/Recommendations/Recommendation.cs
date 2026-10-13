namespace FinanceSentry.Modules.Research.Domain.Recommendations;

using FinanceSentry.Core.Auth;

/// <summary>
/// One proposal Ledger puts to the owner on one ticker (#1069): an action, the evidence behind it and the exact order
/// it implies. The owner confirms or rejects it; Finance Sentry never places, stages or transmits the order - a
/// confirm records the decision and shows the order text for the owner to place at the broker themselves. At most one
/// <see cref="RecommendationStatus.Pending"/> row exists per (UserId, Ticker); a newer proposal supersedes it. Rows are
/// never deleted, so every proposal ends in the decision journal as confirmed, rejected, expired or superseded.
/// </summary>
public class Recommendation : IUserOwned
{
    public const int MaxRationaleLength = 2000;
    public const int MaxInvalidationLength = 500;
    public const int MaxDecisionNoteLength = 2000;
    public const int MaxTriggerRefLength = 120;
    public const int MaxOrderTextLength = 200;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public RecommendationAction Action { get; set; }
    public RecommendationStatus Status { get; set; } = RecommendationStatus.Pending;
    public RecommendationOrigin Origin { get; set; }

    public RecommendationTriggerKind TriggerKind { get; set; }

    /// <summary>What fired, in the trigger's own terms: a thesis id, an asset-class sleeve, a risk rule key, a Radar
    /// scanner or a companion event id.</summary>
    public string TriggerRef { get; set; } = string.Empty;

    public string Rationale { get; set; } = string.Empty;
    public RecommendationConfidence Confidence { get; set; }

    /// <summary>What would make the proposal wrong - the condition under which it should be dropped.</summary>
    public string InvalidationTrigger { get; set; } = string.Empty;

    public List<RecommendationEvidence> Evidence { get; set; } = [];

    // Sizing at proposal time. Null for Hold, which moves nothing.
    public decimal? SizeUsd { get; set; }
    public decimal? SizeShares { get; set; }
    public decimal? ShareOfBookBeforePct { get; set; }
    public decimal? ShareOfBookAfterPct { get; set; }

    /// <summary>The limit price in <see cref="LimitCurrency"/> (the quote's major currency).</summary>
    public decimal? LimitPrice { get; set; }
    public string? LimitCurrency { get; set; }

    /// <summary>Where the owner would place it (the holding's provider, e.g. IBKR) - a label, never an account number.</summary>
    public string? Venue { get; set; }

    /// <summary>The order the owner would type at the broker, e.g. <c>SELL 660 GRAB / LIMIT 3.13 USD DAY / IBKR</c>.
    /// Display text only: nothing in Finance Sentry ever sends it anywhere.</summary>
    public string? OrderText { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public Guid? SupersededById { get; set; }

    // Outcome: did the owner place it (a matching fill after the decision), and how the name did against the S&P 500
    // since the decision.
    public RecommendationExecution Execution { get; set; } = RecommendationExecution.Pending;
    public DateTimeOffset? ExecutedAt { get; set; }
    public decimal? SubjectReturnPct { get; set; }
    public decimal? BenchmarkReturnPct { get; set; }
    public DateTimeOffset? OutcomeScoredAt { get; set; }
}
