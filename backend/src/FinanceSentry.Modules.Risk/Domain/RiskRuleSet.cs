namespace FinanceSentry.Modules.Risk.Domain;

using FinanceSentry.Core.Auth;

/// <summary>Versioned per-user risk rule set (FR-001). A new save appends a row and flips IsCurrent.</summary>
public class RiskRuleSet : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public int Version { get; set; }

    public bool IsCurrent { get; set; }

    public decimal? MaxPositionWeightPct { get; set; }

    public decimal? MaxSleeveWeightPct { get; set; }

    public decimal? MinCashBufferPct { get; set; }

    public decimal? MaxLossPerThesisPct { get; set; }

    public decimal? MaxNewPositionPct { get; set; }

    public int? TurnoverBudgetPerQuarter { get; set; }

    // NOTE (039): target allocation moved to its single home — the IPS
    // (InvestmentPolicyStatement.AllocationTargets, Research module). The Risk rule set holds
    // enforced limits; intent like the target mix lives in the IPS. The allocation-drift check
    // reads the IPS via IAllocationPolicySource. See specs/039-ips-risk-boundary.
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
