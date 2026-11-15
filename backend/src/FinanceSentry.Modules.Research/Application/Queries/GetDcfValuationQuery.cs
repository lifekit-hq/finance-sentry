namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Valuation;
using Microsoft.Extensions.Options;

/// <summary>
/// Discounted-cash-flow valuation for one equity ticker (#1269): a five-year unlevered free-cash-flow model, WACC
/// from CAPM and the market-value weights, a Gordon terminal value, two 5 x 5 sensitivity grids, the revenue growth
/// the current price implies, and an exit-multiple cross-check from <c>get_comps_valuation</c>. The optional
/// overrides (fractions: 0.08 = 8%) replace year-1 growth, operating margin, WACC, terminal growth and the
/// projection length for this call; the result echoes what it actually used. Computed on read from Yahoo; no LLM,
/// no stored row, no zero-filled gap.
/// </summary>
public record GetDcfValuationQuery(
    string Ticker,
    decimal? Growth = null,
    decimal? OperatingMargin = null,
    decimal? Wacc = null,
    decimal? TerminalGrowth = null,
    int? Years = null) : IQuery<DcfValuationResult>;

public class GetDcfValuationQueryHandler(
    IValuationDataService valuation,
    IFundamentalsService fundamentals,
    IDcfInputsSource inputs,
    IMarketDataService marketData,
    IQueryHandler<GetCompsValuationQuery, CompsValuationResult> comps,
    IOptions<ValuationOptions> options,
    TimeProvider clock) : IQueryHandler<GetDcfValuationQuery, DcfValuationResult>
{
    private const string TreasuryYieldSymbol = "^TNX";
    private const string TimeseriesSource = "yahoo:fundamentals-timeseries";
    private const string QuoteSummarySource = "yahoo:quoteSummary";
    private const string TreasurySource = "yahoo:^TNX";

    private const decimal PercentScale = 100m;
    private const int MoneyDecimals = 2;
    private const int RateDecimals = 4;
    private const int PercentDecimals = 1;
    private const int MultipleDecimals = 2;

    private const int HistoryPeriods = 5;
    private const int AveragingYears = 3;
    private const int GridHalfWidth = 2;
    private const decimal WaccStep = 0.005m;
    private const decimal TerminalGrowthStep = 0.005m;
    private const decimal GrowthStep = 0.01m;
    private const decimal MarginStep = 0.01m;
    private const decimal SolveLowGrowth = -0.30m;
    private const decimal SolveHighGrowth = 0.60m;
    private const decimal MaxPlausibleYieldPercent = 25m;
    private const double DaysPerYear = 365.25;

    // The ranges an override must sit in: they catch "8" sent for 8% as well as a typo.
    private const decimal MinGrowthOverride = -0.5m;
    private const decimal MaxGrowthOverride = 1m;
    private const decimal MaxMarginOverride = 1m;
    private const decimal MinWaccOverride = 0.01m;
    private const decimal MaxWaccOverride = 0.5m;
    private const decimal MinTerminalGrowthOverride = -0.05m;
    private const decimal MaxTerminalGrowthOverride = 0.1m;

    private readonly ValuationOptions settings = options.Value;

    // What the history says: the operating drivers and the revenue CAGR.
    private sealed record Derived(DcfMath.Drivers Drivers, decimal Cagr);

    private readonly record struct Failure(string Reason, string Detail);

    public async Task<DcfValuationResult> Handle(GetDcfValuationQuery query, CancellationToken ct)
    {
        var ticker = query.Ticker.Trim().ToUpperInvariant();
        var now = clock.GetUtcNow();

        if (InvalidOverride(query) is { } invalid)
        {
            return DcfValuationResult.NotEvaluable(ticker, null, null, DcfReasons.InvalidOverride, invalid, now);
        }

        var current = await valuation.GetCurrentMetricsAsync(ticker, ct);
        if (current is null)
        {
            return DcfValuationResult.NotEvaluable(ticker, null, null, DcfReasons.DataUnavailable, "no quoteSummary for the ticker", now);
        }

        if (current.NotApplicable)
        {
            return DcfValuationResult.ForNonEquity(ticker, now);
        }

        DcfValuationResult NotEvaluable(string reason, string? detail, IReadOnlyList<string>? caveats)
            => DcfValuationResult.NotEvaluable(ticker, current.Name, current.Currency, reason, detail, now, caveats);

        if (ValuationSectors.IsFinancial(current.Industry))
        {
            return NotEvaluable(DcfReasons.FinancialSector, "free cash flow is not meaningful for banks and insurers", null);
        }

        var coverage = (await fundamentals.GetFundamentalsAsync(ticker, 1, ct)).Coverage;
        if (coverage.Status != FundamentalsCoverageStatus.Covered)
        {
            return NotEvaluable(coverage.Status, coverage.Reason, null);
        }

        if (current.Price is not > 0m)
        {
            return NotEvaluable(DcfReasons.PriceUnavailable, "no current price", null);
        }

        var history = await inputs.GetAsync(ticker, ct);
        if (history is null)
        {
            return NotEvaluable(DcfReasons.DataUnavailable, "no statement history", null);
        }

        var flags = new List<string>();
        if (query.Growth is not null || query.OperatingMargin is not null || query.Wacc is not null
            || query.TerminalGrowth is not null || query.Years is not null)
        {
            flags.Add(DcfFlags.OverridesApplied);
        }

        var (derived, failure) = Derive(history, flags);
        if (derived is null || failure is { })
        {
            return NotEvaluable(failure?.Reason ?? DcfReasons.DataUnavailable, failure?.Detail, flags);
        }

        var bridge = ValuationBridge.Resolve(current, flags);
        if (bridge.NetDebt is not { } netDebt || bridge.ToQuotedUnit is not { } toQuoted)
        {
            return NotEvaluable(DcfReasons.EquityBridgeUnavailable, "net debt or the reporting-currency conversion is missing", flags);
        }

        if (bridge.Shares is not { } shares)
        {
            return NotEvaluable(DcfReasons.SharesUnavailable, "no share count", flags);
        }

        var price = current.Price.Value;
        var drivers = derived.Drivers;
        var margin = query.OperatingMargin ?? drivers.OperatingMargin;
        drivers = drivers with { OperatingMargin = margin };
        var years = query.Years ?? settings.ProjectionYears;

        var sources = new List<string> { TimeseriesSource, QuoteSummarySource };
        var riskFree = await RiskFreeAsync(ct);
        if (riskFree is not null)
        {
            sources.Add(TreasurySource);
        }

        var wacc = query.Wacc;
        if (wacc is null)
        {
            if (riskFree is null || current.Beta is null)
            {
                return NotEvaluable(
                    riskFree is null ? DcfReasons.RiskFreeUnavailable : DcfReasons.BetaUnavailable,
                    "WACC needs the 10-year Treasury yield and the beta; pass a wacc override to proceed without them",
                    flags);
            }

            wacc = BuildWacc(riskFree.Value, current.Beta.Value, derived.Drivers.TaxRate, netDebt, shares * price / toQuoted, history, current, flags);
        }

        var terminalGrowth = query.TerminalGrowth ?? CappedTerminalGrowth(riskFree, wacc.Value, flags);
        var firstYearGrowth = query.Growth ?? CappedGrowth(derived.Cagr, flags);

        DcfMath.Scenario BuildScenario(decimal growth, decimal operatingMargin, decimal discountRate, decimal terminal) => new(
            drivers with { OperatingMargin = operatingMargin },
            DcfMath.FadingGrowthPath(growth, terminal, years),
            discountRate,
            terminal);

        decimal? PerShare(DcfMath.Scenario scenario)
            => DcfMath.Value(scenario) is { } v
                ? CompsMath.PerShareFromEnterpriseValue(v.EnterpriseValue, netDebt, shares) * toQuoted
                : null;

        var baseScenario = BuildScenario(firstYearGrowth, margin, wacc.Value, terminalGrowth);
        var valuationResult = DcfMath.Value(baseScenario);
        if (valuationResult is null)
        {
            return NotEvaluable(
                DcfReasons.TerminalGrowthNotBelowWacc,
                $"terminal growth {terminalGrowth:P2} is not below WACC {wacc.Value:P2}: the Gordon terminal value has no finite value",
                flags);
        }

        if (PerShare(baseScenario) is not { } valuePerShare)
        {
            return NotEvaluable(DcfReasons.NegativeEquityValue, "the equity value after net debt is not positive", flags);
        }

        if (valuationResult.TerminalShare > settings.TerminalHeavyShare)
        {
            flags.Add(DcfFlags.TerminalHeavy);
        }

        var waccByGrowth = BuildGrid(
            "wacc", "terminalGrowth",
            DcfMath.Axis(wacc.Value, WaccStep, GridHalfWidth),
            DcfMath.Axis(terminalGrowth, TerminalGrowthStep, GridHalfWidth),
            (w, g) => PerShare(BuildScenario(firstYearGrowth, margin, w, g)));
        var growthByMargin = BuildGrid(
            "year1Growth", "operatingMargin",
            DcfMath.Axis(firstYearGrowth, GrowthStep, GridHalfWidth),
            DcfMath.Axis(margin, MarginStep, GridHalfWidth),
            (g, m) => PerShare(BuildScenario(g, m, wacc.Value, terminalGrowth)));

        var waccGridValues = waccByGrowth.Values.SelectMany(row => row).OfType<decimal>().ToList();

        var implied = DcfMath.Solve(
            g => PerShare(baseScenario with { GrowthPath = DcfMath.ConstantGrowthPath(g, years) }),
            price,
            SolveLowGrowth,
            SolveHighGrowth);
        if (implied is null)
        {
            flags.Add(DcfFlags.ImpliedGrowthOutOfRange);
        }

        var crossCheck = await ExitMultipleAsync(baseScenario, netDebt, shares, toQuoted, flags, sources, ticker, ct);

        var summary = new DcfSummary(
            Money(valuePerShare),
            price,
            Math.Round((valuePerShare / price - 1m) * PercentScale, PercentDecimals),
            waccGridValues.Count > 0 ? Money(waccGridValues.Min()) : null,
            waccGridValues.Count > 0 ? Money(waccGridValues.Max()) : null,
            implied is { } g1 ? Math.Round(g1 * PercentScale, PercentDecimals) : null,
            Math.Round(derived.Cagr * PercentScale, PercentDecimals),
            Math.Round(valuationResult.TerminalShare * PercentScale, PercentDecimals));

        var assumptions = new DcfAssumptions(
            Math.Round(wacc.Value, RateDecimals),
            Math.Round(terminalGrowth, RateDecimals),
            riskFree is { } rf ? Math.Round(rf, RateDecimals) : null,
            current.Beta,
            settings.EquityRiskPremium,
            Math.Round(drivers.TaxRate, RateDecimals),
            years,
            baseScenario.GrowthPath.Select(g => Math.Round(g, RateDecimals)).ToList(),
            Math.Round(margin, RateDecimals),
            Math.Round(drivers.DepreciationPct, RateDecimals),
            Math.Round(drivers.CapexPct, RateDecimals),
            Math.Round(drivers.NwcPctOfRevenueChange, RateDecimals),
            Math.Round(drivers.BaseRevenue, 0));

        return new DcfValuationResult(
            ticker,
            NotApplicable: false,
            current.Name,
            current.Currency,
            NotEvaluableReason: null,
            Detail: null,
            summary,
            assumptions,
            new DcfSensitivity(waccByGrowth, growthByMargin),
            crossCheck,
            flags.Distinct().ToList(),
            sources.Distinct().ToList(),
            now);
    }

    // --- overrides ---

    private static string? InvalidOverride(GetDcfValuationQuery q)
    {
        if (q.Growth is { } g && g is < MinGrowthOverride or > MaxGrowthOverride)
        {
            return $"growth must be a fraction between {MinGrowthOverride} and {MaxGrowthOverride} (0.08 = 8%), got {g}";
        }

        if (q.OperatingMargin is { } m && m is <= 0m or > MaxMarginOverride)
        {
            return $"operatingMargin must be a fraction above 0 and at most {MaxMarginOverride} (0.20 = 20%), got {m}";
        }

        if (q.Wacc is { } w && w is < MinWaccOverride or > MaxWaccOverride)
        {
            return $"wacc must be a fraction between {MinWaccOverride} and {MaxWaccOverride} (0.09 = 9%), got {w}";
        }

        if (q.TerminalGrowth is { } t && t is < MinTerminalGrowthOverride or > MaxTerminalGrowthOverride)
        {
            return $"terminalGrowth must be a fraction between {MinTerminalGrowthOverride} and {MaxTerminalGrowthOverride} (0.025 = 2.5%), got {t}";
        }

        if (q.Years is { } y && y is < ValuationOptions.MinProjectionYears or > ValuationOptions.MaxProjectionYears)
        {
            return $"years must be between {ValuationOptions.MinProjectionYears} and {ValuationOptions.MaxProjectionYears}, got {y}";
        }

        return null;
    }

    // --- history -> drivers ---

    private (Derived? Value, Failure? Failure) Derive(DcfInputs inputs, List<string> flags)
    {
        var periods = inputs.Annual
            .Where(p => p.Revenue is > 0m && p.OperatingIncome is not null)
            .Take(HistoryPeriods)
            .ToList();
        if (periods.Count < AveragingYears)
        {
            return (null, new Failure(
                DcfReasons.InsufficientHistory,
                $"{periods.Count} annual periods with revenue and operating income; {AveragingYears} are needed"));
        }

        var latest = periods[0];
        var window = periods.Take(AveragingYears).ToList();
        var margin = window.Average(p => p.OperatingIncome!.Value / p.Revenue!.Value);
        if (latest.OperatingIncome <= 0m || margin <= 0m)
        {
            return (null, new Failure(DcfReasons.NoOperatingProfit, "the latest annual operating income or the 3-year average margin is not positive"));
        }

        var depreciation = Mean(window, p => Ratio(p.DepreciationAndAmortization, p.Revenue));
        var capex = Mean(window, p => Ratio(p.CapitalExpenditure, p.Revenue));
        if (depreciation is null || capex is null)
        {
            return (null, new Failure(DcfReasons.CashFlowUnavailable, "depreciation and amortization or capital expenditure is missing"));
        }

        var baseRevenue = latest.Revenue!.Value;
        if (inputs.TtmRevenue is { } ttm && inputs.LatestQuarterEnd is { } quarterEnd
            && quarterEnd.DayNumber - latest.PeriodEnd.DayNumber >= settings.TtmMinGapDays)
        {
            baseRevenue = ttm;
            flags.Add(DcfFlags.BaseYearTtm);
        }

        var drivers = new DcfMath.Drivers(
            baseRevenue,
            margin,
            EffectiveTaxRate(periods, flags),
            Math.Abs(depreciation.Value),
            Math.Abs(capex.Value),
            WorkingCapitalRatio(periods, flags));

        var oldest = periods[^1];
        var spanYears = (decimal)((latest.PeriodEnd.DayNumber - oldest.PeriodEnd.DayNumber) / DaysPerYear);
        var cagr = (decimal)(Math.Pow((double)(latest.Revenue.Value / oldest.Revenue!.Value), 1d / (double)spanYears) - 1d);
        return (new Derived(drivers, cagr), null);
    }

    private static decimal? Ratio(decimal? amount, decimal? revenue)
        => amount is { } a && revenue is > 0m ? a / revenue.Value : null;

    private static decimal? Mean(IEnumerable<DcfAnnualPeriod> periods, Func<DcfAnnualPeriod, decimal?> ratio)
    {
        var values = periods.Select(ratio).OfType<decimal>().ToList();
        return values.Count == 0 ? null : values.Average();
    }

    // Tax provision over pretax income, summed over the history: one loss-making year does not skew it. Missing, or
    // outside 0 to the maximum, falls back to the default rate (flag TAX_RATE_DEFAULTED).
    private decimal EffectiveTaxRate(IReadOnlyList<DcfAnnualPeriod> periods, List<string> flags)
    {
        var profitable = periods.Where(p => p.TaxProvision is not null && p.PretaxIncome is > 0m).ToList();
        if (profitable.Count > 0)
        {
            var rate = profitable.Sum(p => p.TaxProvision!.Value) / profitable.Sum(p => p.PretaxIncome!.Value);
            if (rate >= 0m && rate <= settings.MaxTaxRate)
            {
                return rate;
            }
        }

        flags.Add(DcfFlags.TaxRateDefaulted);
        return settings.DefaultTaxRate;
    }

    // Cash tied up in working capital per unit of revenue growth, over the last three year-on-year steps. Yahoo's
    // "change in working capital" is a cash-flow line (an outflow is negative), so the investment is its negation.
    // Not derivable, or beyond +-100% of the revenue change (near-flat revenue over lumpy working capital) -> 0 with
    // a flag, never a silent zero.
    private const decimal MaxWorkingCapitalRatio = 1m;

    private static decimal WorkingCapitalRatio(IReadOnlyList<DcfAnnualPeriod> periods, List<string> flags)
    {
        decimal investment = 0m;
        decimal revenueChange = 0m;
        for (var i = 0; i < Math.Min(AveragingYears, periods.Count - 1); i++)
        {
            if (periods[i].ChangeInWorkingCapital is { } change)
            {
                investment -= change;
                revenueChange += periods[i].Revenue!.Value - periods[i + 1].Revenue!.Value;
            }
        }

        if (revenueChange > 0m && Math.Abs(investment / revenueChange) <= MaxWorkingCapitalRatio)
        {
            return investment / revenueChange;
        }

        flags.Add(DcfFlags.NwcAssumedZero);
        return 0m;
    }

    // --- discount rate ---

    private decimal BuildWacc(
        decimal riskFree,
        decimal beta,
        decimal taxRate,
        decimal netDebt,
        decimal equityValue,
        DcfInputs history,
        ValuationCurrentMetrics current,
        List<string> flags)
    {
        var costOfEquity = DcfMath.CostOfEquity(riskFree, beta, settings.EquityRiskPremium);
        if (netDebt <= 0m)
        {
            return costOfEquity;
        }

        var interest = history.Annual.Select(p => p.InterestExpense).FirstOrDefault(i => i is not null);
        var debt = current.Comps?.TotalDebt;
        decimal preTaxCostOfDebt;
        if (interest is > 0m && debt is > 0m)
        {
            preTaxCostOfDebt = interest.Value / debt.Value;
        }
        else
        {
            preTaxCostOfDebt = riskFree + settings.DefaultDebtSpread;
            flags.Add(DcfFlags.CostOfDebtDefaulted);
        }

        return DcfMath.Wacc(costOfEquity, preTaxCostOfDebt * (1m - taxRate), equityValue, netDebt);
    }

    // Terminal growth never exceeds the risk-free rate or sits closer than the spread to WACC.
    private decimal CappedTerminalGrowth(decimal? riskFree, decimal wacc, List<string> flags)
    {
        var ceiling = Math.Min(riskFree ?? decimal.MaxValue, wacc - settings.TerminalGrowthWaccSpread);
        if (settings.TerminalGrowth <= ceiling)
        {
            return settings.TerminalGrowth;
        }

        flags.Add(DcfFlags.TerminalGrowthCapped);
        return ceiling;
    }

    private decimal CappedGrowth(decimal cagr, List<string> flags)
    {
        if (cagr <= settings.Year1GrowthCap)
        {
            return cagr;
        }

        flags.Add(DcfFlags.GrowthCapped);
        return settings.Year1GrowthCap;
    }

    // The 10-year Treasury yield from the shared quote path (^TNX quotes the yield in percent).
    private async Task<decimal?> RiskFreeAsync(CancellationToken ct)
    {
        var quotes = await marketData.GetQuotesAsync([TreasuryYieldSymbol], ct);
        return quotes.TryGetValue(TreasuryYieldSymbol, out var quote) && quote.Price is > 0m and < MaxPlausibleYieldPercent
            ? quote.Price / PercentScale
            : null;
    }

    // --- grids, cross-check ---

    private static DcfGrid BuildGrid(
        string rowAxis, string colAxis, IReadOnlyList<decimal> rows, IReadOnlyList<decimal> cols, Func<decimal, decimal, decimal?> cell)
        => new(
            rowAxis,
            colAxis,
            rows.Select(r => Math.Round(r, RateDecimals)).ToList(),
            cols.Select(c => Math.Round(c, RateDecimals)).ToList(),
            DcfMath.Grid(rows, cols, cell)
                .Select(row => (IReadOnlyList<decimal?>)row.Select(v => v is { } value ? Money(value) : (decimal?)null).ToList())
                .ToList());

    private async Task<DcfExitMultipleCrossCheck> ExitMultipleAsync(
        DcfMath.Scenario scenario,
        decimal netDebt,
        decimal shares,
        decimal toQuoted,
        List<string> flags,
        List<string> sources,
        string ticker,
        CancellationToken ct)
    {
        var peers = await comps.Handle(new GetCompsValuationQuery(ticker, null), ct);
        var multiple = peers.Multiples?.EvToEbitda;
        if (multiple is not { NotEvaluable: false, Median: { } median })
        {
            flags.Add(DcfFlags.ExitMultipleUnavailable);
            return new DcfExitMultipleCrossCheck(null, null, multiple?.NotEvaluableReason ?? peers.NotEvaluableReason ?? DcfReasons.DataUnavailable);
        }

        sources.AddRange(peers.Sources);
        var enterprise = DcfMath.EnterpriseValueAtExitMultiple(scenario, median);
        var perShare = enterprise is { } ev ? CompsMath.PerShareFromEnterpriseValue(ev, netDebt, shares) * toQuoted : null;
        if (perShare is null)
        {
            flags.Add(DcfFlags.ExitMultipleUnavailable);
        }

        return new DcfExitMultipleCrossCheck(
            Math.Round(median, MultipleDecimals),
            perShare is { } value ? Money(value) : null,
            perShare is null ? DcfReasons.NegativeEquityValue : null);
    }

    private static decimal Money(decimal value) => Math.Round(value, MoneyDecimals);
}
