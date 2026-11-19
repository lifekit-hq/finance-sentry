namespace FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// Closed vocabulary of metrics that a <see cref="ThesisInvalidationTrigger"/> may reference.
/// Any metric outside this set is rejected at save time and non-evaluable at run time.
/// </summary>
public static class ThesisMetric
{
    public const string GrossMargin = "gross_margin";
    public const string OperatingMargin = "operating_margin";
    public const string NetMargin = "net_margin";
    public const string RevenueYoy = "revenue_yoy";
    public const string NetIncomeYoy = "net_income_yoy";
    public const string OperatingIncomeYoy = "operating_income_yoy";
    public const string EpsYoy = "eps_yoy";
    public const string Revenue = "revenue";
    public const string NetIncome = "net_income";
    public const string DilutedEps = "diluted_eps";
    public const string PriceDrawdown = "price_drawdown";
    public const string PriceReturn = "price_return";

    /// <summary>Subject return minus a named benchmark's return over the same trailing window (#697).</summary>
    public const string RelativeReturn = "relative_return";

    private static readonly IReadOnlySet<string> FundamentalsMetrics = new HashSet<string>(StringComparer.Ordinal)
    {
        GrossMargin,
        OperatingMargin,
        NetMargin,
        RevenueYoy,
        NetIncomeYoy,
        OperatingIncomeYoy,
        EpsYoy,
        Revenue,
        NetIncome,
        DilutedEps,
    };

    private static readonly IReadOnlySet<string> PriceMetrics = new HashSet<string>(StringComparer.Ordinal)
    {
        PriceDrawdown,
        PriceReturn,
    };

    private static readonly IReadOnlySet<string> RelativeMetrics = new HashSet<string>(StringComparer.Ordinal)
    {
        RelativeReturn,
    };

    private static readonly IReadOnlySet<string> AllMetrics = new HashSet<string>(
        FundamentalsMetrics.Concat(PriceMetrics).Concat(RelativeMetrics), StringComparer.Ordinal);

    /// <summary>Every metric a trigger may reference, for a prompt that must name the closed vocabulary.</summary>
    public static IReadOnlyCollection<string> All => AllMetrics;

    public static bool Contains(string metric) => AllMetrics.Contains(metric);

    public static bool IsPriceMetric(string metric) => PriceMetrics.Contains(metric);

    /// <summary>
    /// Whether a breach of this metric may mark a thesis broken (and so lead to a rules Exit). Price and relative-return
    /// breaches only open a review: exits follow a broken thesis, a risk breach or a better use of capital, never P&amp;L.
    /// </summary>
    public static bool CanBreakThesis(string metric) => !IsPriceMetric(metric) && !IsRelativeMetric(metric);

    /// <summary>
    /// Whether a stored break reason names a metric that cannot break a thesis: a break stored before price and
    /// relative-return breaches stopped breaking theses. Such a break is a review, not a real break.
    /// </summary>
    public static bool IsNonBreakingReason(string? reason)
        => reason is not null
            && AllMetrics.Where(m => !CanBreakThesis(m))
                .Any(m => reason.StartsWith(m, StringComparison.OrdinalIgnoreCase));

    public static bool IsRelativeMetric(string metric) => RelativeMetrics.Contains(metric);
}
