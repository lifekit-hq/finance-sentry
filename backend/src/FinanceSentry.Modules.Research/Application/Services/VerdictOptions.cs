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
    public int RulesVersion { get; set; } = 2;

    /// <summary>Adjusted structure score below this is AVOID.</summary>
    public int AvoidStructureBelow { get; set; } = 30;

    /// <summary>Adjusted structure score at or above this is needed for STRONG BUY.</summary>
    public int StrongBuyStructureAtLeast { get; set; } = 70;

    /// <summary>Fundamentals score at or above this is needed for STRONG BUY.</summary>
    public int StrongBuyFundamentalsAtLeast { get; set; } = 70;

    /// <summary>
    /// The most the price lens may read above its yardstick (percent) for STRONG BUY: the trailing P/E over its own
    /// five-year average, or, when that history is missing, the forward P/E over the peer median.
    /// </summary>
    public decimal StrongBuyMaxValuationPremiumPct { get; set; } = 10m;

    /// <summary>Adjusted structure score at or above this is needed for BUY.</summary>
    public int BuyStructureAtLeast { get; set; } = 50;

    /// <summary>Fundamentals score at or above this is needed for BUY.</summary>
    public int BuyFundamentalsAtLeast { get; set; } = 50;

    /// <summary>Revenue growth year over year, in percent, needed for STRONG BUY (v2 growth gate).</summary>
    public decimal StrongBuyMinRevenueYoyPct { get; set; } = 10m;

    /// <summary>
    /// Fundamentals score at or above this is a strong business: with a trend below the BUY bar the word is
    /// the named WAIT "strong business, weak trend".
    /// </summary>
    public int WaitStrongBusinessFundamentalsAtLeast { get; set; } = 70;

    /// <summary>
    /// Peers with a positive forward P/E needed before the peer median counts as a price lens; fewer and the
    /// lens is missing, never a median of one or two names.
    /// </summary>
    public int MinPeersForPriceLens { get; set; } = 3;

    /// <summary>
    /// Version of the ETF rule table (<c>EtfVerdictRules</c>), carried on an ETF verdict. The ETF table reads
    /// <see cref="AvoidStructureBelow"/> and <see cref="BuyStructureAtLeast"/> for the fund's own structure.
    /// </summary>
    public int EtfRulesVersion { get; set; } = 1;

    /// <summary>
    /// More than this share (percent) of an ETF's top-10 weight must read BUY or better for the fund to read BUY
    /// ("most of the top-10 weight").
    /// </summary>
    public decimal EtfBuyWeightSharePctAbove { get; set; } = 50m;
}
