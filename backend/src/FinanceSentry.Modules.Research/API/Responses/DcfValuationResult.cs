namespace FinanceSentry.Modules.Research.API.Responses;

/// <summary>
/// The short reply of a DCF: value against price, the range of the WACC x terminal-growth grid, what growth the
/// price implies against what the company delivered, and how much of the value is terminal. Amounts are in the
/// quote currency; the <c>…Pct</c> fields are percentages (5.0 = 5%).
/// </summary>
public record DcfSummary(
    decimal ValuePerShare,
    decimal Price,
    decimal UpsidePct,
    decimal? RangeLow,
    decimal? RangeHigh,
    decimal? ImpliedGrowthPct,
    decimal HistoricalCagrPct,
    decimal TerminalShareOfEvPct);

/// <summary>
/// The assumptions the number actually used (a client's overrides included). Rates are fractions (0.09 = 9%).
/// <see cref="GrowthPath"/> is the revenue growth of each projected year; the depreciation, capex and working-capital
/// ratios are held at their historical averages. <see cref="RiskFree"/> and <see cref="Beta"/> are null when a
/// WACC override made them unnecessary and the source had none.
/// </summary>
public record DcfAssumptions(
    decimal Wacc,
    decimal TerminalGrowth,
    decimal? RiskFree,
    decimal? Beta,
    decimal EquityRiskPremium,
    decimal TaxRate,
    int Years,
    IReadOnlyList<decimal> GrowthPath,
    decimal OperatingMargin,
    decimal DepreciationPct,
    decimal CapexPct,
    decimal NwcPctOfRevenueChange,
    decimal BaseRevenue);

/// <summary>
/// A 5 x 5 sensitivity grid of value per share; every cell is a full recompute, the centre cell is the headline.
/// A cell the model refuses (terminal growth at or above WACC) is null. <see cref="Rows"/> and <see cref="Cols"/>
/// are fractions named by <see cref="RowAxis"/> and <see cref="ColAxis"/>.
/// </summary>
public record DcfGrid(
    string RowAxis,
    string ColAxis,
    IReadOnlyList<decimal> Rows,
    IReadOnlyList<decimal> Cols,
    IReadOnlyList<IReadOnlyList<decimal?>> Values);

public record DcfSensitivity(DcfGrid WaccByGrowth, DcfGrid GrowthByMargin);

/// <summary>
/// The terminal value re-done as final-year EBITDA x the peer-median EV/EBITDA from <c>get_comps_valuation</c>.
/// Null figures with <see cref="UnavailableReason"/> when the comps tool has no usable median.
/// </summary>
public record DcfExitMultipleCrossCheck(decimal? EvToEbitda, decimal? ValuePerShare, string? UnavailableReason);

/// <summary>
/// Discounted-cash-flow valuation for one equity ticker (#1269). Computed on read; nothing is stored and nothing is
/// zero-filled: <see cref="NotApplicable"/> = non-equity, <see cref="NotEvaluableReason"/> = an equity the model
/// refuses (with <see cref="Detail"/> saying why), and in both cases every figure is null.
/// </summary>
public record DcfValuationResult(
    string Ticker,
    bool NotApplicable,
    string? Name,
    string? Currency,
    string? NotEvaluableReason,
    string? Detail,
    DcfSummary? Summary,
    DcfAssumptions? Assumptions,
    DcfSensitivity? Sensitivity,
    DcfExitMultipleCrossCheck? ExitMultipleCrossCheck,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Sources,
    DateTimeOffset RetrievedAt)
{
    public static DcfValuationResult ForNonEquity(string ticker, DateTimeOffset now)
        => Empty(ticker, notApplicable: true, name: null, currency: null, reason: null, detail: null, now);

    public static DcfValuationResult NotEvaluable(
        string ticker, string? name, string? currency, string reason, string? detail, DateTimeOffset now,
        IReadOnlyList<string>? flags = null, IReadOnlyList<string>? sources = null)
        => Empty(ticker, notApplicable: false, name, currency, reason, detail, now) with
        {
            Flags = flags ?? [],
            Sources = sources ?? [],
        };

    private static DcfValuationResult Empty(
        string ticker, bool notApplicable, string? name, string? currency, string? reason, string? detail, DateTimeOffset now)
        => new(
            ticker.Trim().ToUpperInvariant(),
            notApplicable,
            name,
            currency,
            reason,
            detail,
            Summary: null,
            Assumptions: null,
            Sensitivity: null,
            ExitMultipleCrossCheck: null,
            Flags: [],
            Sources: [],
            RetrievedAt: now);
}
