namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// The user's Investment Policy Statement — the written, versioned strategy that
/// every proactive alert and rebalancing prompt is measured against. Structure follows
/// the CFA Institute / Bogleheads IPS schema: purpose, horizon, risk, target allocation
/// with rebalancing bands, contributions, sell discipline, and constraints.
/// Amended over time: saving a new version supersedes the prior one (IsCurrent flips),
/// keeping history rather than overwriting.
/// </summary>
public class InvestmentPolicyStatement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    // Versioning — an IPS is revised, not rewritten; prior versions are retained.
    public int Version { get; set; } = 1;
    public bool IsCurrent { get; set; } = true;

    // Purpose — every allocation decision traces to a goal + when the money is needed.
    public List<InvestmentGoal> Goals { get; set; } = [];

    // Horizon & liquidity.
    public int PrimaryHorizonYears { get; set; }
    public decimal? EmergencyCushionUsd { get; set; }

    // Risk — tolerance (behavioral, 1..5) kept separate from capacity (afford, 1..5).
    public int RiskTolerance { get; set; }
    public int? RiskCapacity { get; set; }
    public decimal? MaxDrawdownTolerancePct { get; set; }

    /// <summary>
    /// When tolerance, capacity and drawdown tolerance were last measured with the user (#700). Set by
    /// a re-measurement or by a save that changed any of the three; carried forward unchanged by a
    /// save that did not touch them, so a version written for another reason never passes for a
    /// fresh measurement. Null = never measured (every statement authored before #700).
    /// </summary>
    public DateTimeOffset? RiskMeasuredAt { get; set; }

    // Target allocation + how/when to rebalance back to it.
    public List<AllocationTarget> AllocationTargets { get; set; } = [];
    public RebalancingRule RebalancingRule { get; set; } = RebalancingRule.Default;

    // Ongoing contributions (dollar-cost averaging).
    public ContributionPlan? ContributionPlan { get; set; }

    // Sell discipline — the anti-panic mechanism.
    public string? SellDiscipline { get; set; }
    public int CoolingOffDays { get; set; } = DefaultCoolingOffDays;

    // Constraints.
    // NOTE (039): the single-position cap moved to its single home — the Risk rule set
    // (RiskRuleSet.MaxPositionWeightPct). The IPS holds intent (target allocation); enforced
    // limits like the position cap live in Risk. See specs/039-ips-risk-boundary.
    public List<string> Exclusions { get; set; } = [];

    // Governance / review.
    public string ReviewCadence { get; set; } = "annual";
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    private const int DefaultCoolingOffDays = 90;
}

/// <summary>A funding goal — what the money is for and when it is needed.</summary>
public record InvestmentGoal(string Label, decimal? TargetAmountUsd, DateOnly? TargetDate, string Priority);

/// <summary>
/// A target weight for an asset class, with the min/max band that triggers a rebalance.
/// </summary>
public record AllocationTarget(string AssetClass, decimal TargetPct, decimal MinPct, decimal MaxPct);

/// <summary>
/// How and when to rebalance. Defaults to the industry-standard 5/25 rule
/// (rebalance when a sleeve drifts 5 absolute points OR 25% relative, whichever first),
/// annual review cadence, and directing new contributions to underweight sleeves first
/// to minimise tax drag.
/// </summary>
public record RebalancingRule(decimal AbsoluteBandPct, decimal RelativeBandPct, string Cadence, bool ContributionsFirst)
{
    public static RebalancingRule Default => new(5m, 25m, "annual", true);
}

/// <summary>A recurring contribution — the DCA plan.</summary>
public record ContributionPlan(decimal AmountUsd, string Cadence);
