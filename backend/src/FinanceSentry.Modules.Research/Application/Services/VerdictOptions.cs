namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// Thresholds of the verdict decision list (spec 726 OQ-4), bound from configuration (section
/// <c>Verdict</c>). Structure thresholds apply to the regime-adjusted structure score (OQ-8). Any
/// change to a threshold or to the rule order bumps <see cref="RulesVersion"/>, so every verdict names
/// the table that produced it (FR-006).
/// </summary>
public sealed class VerdictOptions
{
    public const string SectionName = "Verdict";

    /// <summary>Version of the rule table; carried on every verdict.</summary>
    public int RulesVersion { get; set; } = 1;

    /// <summary>Adjusted structure score below this is AVOID.</summary>
    public int AvoidStructureBelow { get; set; } = 30;

    /// <summary>Adjusted structure score at or above this is needed for STRONG BUY.</summary>
    public int StrongBuyStructureAtLeast { get; set; } = 70;

    /// <summary>Fundamentals score at or above this is needed for STRONG BUY.</summary>
    public int StrongBuyFundamentalsAtLeast { get; set; } = 70;

    /// <summary>
    /// The most the trailing P/E may sit above its own five-year average (percent) for STRONG BUY.
    /// </summary>
    public decimal StrongBuyMaxValuationPremiumPct { get; set; } = 10m;

    /// <summary>Adjusted structure score at or above this is needed for BUY.</summary>
    public int BuyStructureAtLeast { get; set; } = 50;

    /// <summary>Fundamentals score at or above this is needed for BUY.</summary>
    public int BuyFundamentalsAtLeast { get; set; } = 50;
}
