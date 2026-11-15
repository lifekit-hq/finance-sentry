namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// Defaults of the DCF (section <c>Valuation</c>, #1269). Every value is a fraction (0.05 = 5%) unless it says
/// otherwise; the client's overrides replace the matching assumption for one call and are echoed back.
/// </summary>
public sealed class ValuationOptions
{
    public const string SectionName = "Valuation";

    public const int MinProjectionYears = 3;
    public const int MaxProjectionYears = 10;

    /// <summary>Projection length (allowed range 3-10).</summary>
    public int ProjectionYears { get; set; } = 5;

    public decimal EquityRiskPremium { get; set; } = 0.05m;

    /// <summary>Terminal growth, capped at the lower of the risk-free rate and WACC less <see cref="TerminalGrowthWaccSpread"/>.</summary>
    public decimal TerminalGrowth { get; set; } = 0.025m;

    public decimal TerminalGrowthWaccSpread { get; set; } = 0.01m;

    /// <summary>A higher historical revenue CAGR is clamped to this for year 1 (flag <c>GROWTH_CAPPED</c>).</summary>
    public decimal Year1GrowthCap { get; set; } = 0.25m;

    /// <summary>Tax rate when the effective rate is not derivable or outside 0 to <see cref="MaxTaxRate"/>.</summary>
    public decimal DefaultTaxRate { get; set; } = 0.21m;

    public decimal MaxTaxRate { get; set; } = 0.40m;

    /// <summary>Spread over the risk-free rate used as the cost of debt when interest expense is missing.</summary>
    public decimal DefaultDebtSpread { get; set; } = 0.02m;

    /// <summary>Terminal value above this share of enterprise value flags <c>TERMINAL_HEAVY</c>.</summary>
    public decimal TerminalHeavyShare { get; set; } = 0.75m;

    /// <summary>The base year is the trailing twelve months when the newest quarter ends this many days after the latest annual period.</summary>
    public int TtmMinGapDays { get; set; } = 120;
}
