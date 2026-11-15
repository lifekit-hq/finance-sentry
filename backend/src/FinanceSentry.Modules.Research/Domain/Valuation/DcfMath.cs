namespace FinanceSentry.Modules.Research.Domain.Valuation;

/// <summary>
/// Pure discounted-cash-flow math for <c>get_dcf_valuation</c> (#1269). Five-year (3-10) unlevered free-cash-flow
/// projection, mid-year discounting (periods 0.5, 1.5, ...), a Gordon-growth terminal value on the year after the
/// last projected year (discounted over the last cash flow's period) that refuses when g >= WACC, an exit-multiple
/// terminal value for the cross-check, and a bisection solve for the growth rate a price implies. Every grid cell
/// and every solve step is a full recompute through <see cref="Value"/> - never a linear approximation - so the
/// centre cell of a grid is the headline by construction. Deterministic; amounts stay in whatever unit the caller
/// supplies (the reporting currency), and nothing here reaches a network or the clock.
/// </summary>
public static class DcfMath
{
    private const decimal MidYearShift = 0.5m;
    private const int BisectionSteps = 80;

    /// <summary>The per-year operating drivers: ratios of revenue, held flat across the projection (base case).</summary>
    public sealed record Drivers(
        decimal BaseRevenue,
        decimal OperatingMargin,
        decimal TaxRate,
        decimal DepreciationPct,
        decimal CapexPct,
        decimal NwcPctOfRevenueChange);

    /// <summary>One full valuation input: the drivers, a revenue-growth rate per projected year, WACC and terminal growth.</summary>
    public sealed record Scenario(Drivers Drivers, IReadOnlyList<decimal> GrowthPath, decimal Wacc, decimal TerminalGrowth);

    /// <summary>One projected year. <see cref="DiscountFactor"/> uses the mid-year period (year - 0.5).</summary>
    public sealed record ProjectedYear(
        int Year,
        decimal Revenue,
        decimal Ebit,
        decimal Depreciation,
        decimal Capex,
        decimal DeltaNwc,
        decimal FreeCashFlow,
        decimal DiscountFactor,
        decimal PresentValue);

    /// <summary>The enterprise value and what it is made of. <see cref="TerminalShare"/> is the terminal value's share of EV (0-1).</summary>
    public sealed record Valuation(
        decimal EnterpriseValue,
        decimal PresentValueOfCashFlows,
        decimal TerminalValue,
        decimal PresentValueOfTerminal,
        decimal TerminalShare,
        IReadOnlyList<ProjectedYear> Years)
    {
        /// <summary>EBITDA of the last projected year: operating income plus depreciation and amortization.</summary>
        public decimal FinalYearEbitda => Years[^1].Ebit + Years[^1].Depreciation;
    }

    /// <summary>Discount factor at a (possibly fractional) period: 1 / (1 + wacc)^period.</summary>
    public static decimal DiscountFactor(decimal wacc, decimal period)
        => (decimal)Math.Pow((double)(1m + wacc), -(double)period);

    /// <summary>The mid-year period of a 1-based projection year: 0.5 for year 1, 1.5 for year 2, ...</summary>
    public static decimal MidYearPeriod(int year) => year - MidYearShift;

    /// <summary>
    /// Year-1 growth fading in a straight line to <paramref name="terminalGrowth"/> in the last year. One year
    /// is just <paramref name="firstYear"/>.
    /// </summary>
    public static IReadOnlyList<decimal> FadingGrowthPath(decimal firstYear, decimal terminalGrowth, int years)
    {
        if (years <= 1)
        {
            return [firstYear];
        }

        return Enumerable.Range(0, years)
            .Select(i => firstYear + (terminalGrowth - firstYear) * i / (years - 1))
            .ToList();
    }

    /// <summary>The same growth rate every year (the reverse DCF's unknown).</summary>
    public static IReadOnlyList<decimal> ConstantGrowthPath(decimal growth, int years)
        => Enumerable.Repeat(growth, years).ToList();

    /// <summary>Cost of equity (CAPM): risk-free rate + beta x equity risk premium.</summary>
    public static decimal CostOfEquity(decimal riskFree, decimal beta, decimal equityRiskPremium)
        => riskFree + beta * equityRiskPremium;

    /// <summary>
    /// WACC weighted by the market value of equity and net debt. With net cash (net debt of zero or less) the
    /// debt weight is 0 and WACC is the cost of equity exactly.
    /// </summary>
    public static decimal Wacc(decimal costOfEquity, decimal afterTaxCostOfDebt, decimal equityMarketValue, decimal netDebt)
    {
        if (netDebt <= 0m || equityMarketValue <= 0m)
        {
            return costOfEquity;
        }

        var total = equityMarketValue + netDebt;
        return costOfEquity * equityMarketValue / total + afterTaxCostOfDebt * netDebt / total;
    }

    /// <summary>
    /// Enterprise value of <paramref name="scenario"/>, or null when it cannot be computed: terminal growth at or
    /// above WACC (the Gordon formula has no finite value), or an empty projection.
    /// </summary>
    public static Valuation? Value(Scenario scenario)
    {
        if (scenario.TerminalGrowth >= scenario.Wacc || scenario.Wacc <= -1m || scenario.GrowthPath.Count == 0)
        {
            return null;
        }

        var years = Project(scenario);
        var pvFlows = years.Sum(y => y.PresentValue);
        var last = years[^1];
        var terminalValue = last.FreeCashFlow * (1m + scenario.TerminalGrowth) / (scenario.Wacc - scenario.TerminalGrowth);
        var pvTerminal = terminalValue * last.DiscountFactor;
        var enterprise = pvFlows + pvTerminal;
        return new Valuation(
            enterprise,
            pvFlows,
            terminalValue,
            pvTerminal,
            enterprise > 0m ? pvTerminal / enterprise : 0m,
            years);
    }

    /// <summary>
    /// Enterprise value with the terminal value taken as final-year EBITDA x <paramref name="exitMultiple"/>,
    /// discounted from the end of the last year (the multiple prices the business at that date). The explicit
    /// cash flows are discounted exactly as in <see cref="Value"/>. Null when there is no projection.
    /// </summary>
    public static decimal? EnterpriseValueAtExitMultiple(Scenario scenario, decimal exitMultiple)
    {
        if (scenario.GrowthPath.Count == 0 || scenario.Wacc <= -1m)
        {
            return null;
        }

        var years = Project(scenario);
        var pvFlows = years.Sum(y => y.PresentValue);
        var finalEbitda = years[^1].Ebit + years[^1].Depreciation;
        return pvFlows + finalEbitda * exitMultiple * DiscountFactor(scenario.Wacc, scenario.GrowthPath.Count);
    }

    /// <summary>
    /// A rectangular grid, one full recompute per cell: <c>result[row][col] = cell(rows[row], cols[col])</c>. A
    /// cell the model refuses (null) stays null.
    /// </summary>
    public static decimal?[][] Grid(
        IReadOnlyList<decimal> rows, IReadOnlyList<decimal> cols, Func<decimal, decimal, decimal?> cell)
        => rows.Select(r => cols.Select(c => cell(r, c)).ToArray()).ToArray();

    /// <summary>
    /// <paramref name="center"/> plus <c>k x step</c> for k = -<paramref name="halfWidth"/>..<paramref name="halfWidth"/>:
    /// a symmetric axis whose middle entry is the base value.
    /// </summary>
    public static IReadOnlyList<decimal> Axis(decimal center, decimal step, int halfWidth)
        => Enumerable.Range(-halfWidth, 2 * halfWidth + 1).Select(k => center + k * step).ToList();

    /// <summary>
    /// The value of the unknown (a growth rate) at which <paramref name="perShareAt"/> equals
    /// <paramref name="target"/>, by bisection over [<paramref name="low"/>, <paramref name="high"/>]. The value must
    /// rise with the unknown; a null value (the model refuses, or equity is negative) counts as below any target.
    /// Null when the target is not bracketed by the interval.
    /// </summary>
    public static decimal? Solve(Func<decimal, decimal?> perShareAt, decimal target, decimal low, decimal high)
    {
        if (perShareAt(low) is { } atLow && atLow > target)
        {
            return null;
        }

        if (perShareAt(high) is not { } atHigh || atHigh < target)
        {
            return null;
        }

        for (var step = 0; step < BisectionSteps; step++)
        {
            var mid = (low + high) / 2m;
            if (perShareAt(mid) is { } value && value >= target)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return (low + high) / 2m;
    }

    private static List<ProjectedYear> Project(Scenario scenario)
    {
        var d = scenario.Drivers;
        var years = new List<ProjectedYear>(scenario.GrowthPath.Count);
        var previousRevenue = d.BaseRevenue;
        for (var i = 0; i < scenario.GrowthPath.Count; i++)
        {
            var year = i + 1;
            var revenue = previousRevenue * (1m + scenario.GrowthPath[i]);
            var ebit = revenue * d.OperatingMargin;
            var tax = Math.Max(ebit, 0m) * d.TaxRate;
            var depreciation = revenue * d.DepreciationPct;
            var capex = revenue * d.CapexPct;
            var deltaNwc = d.NwcPctOfRevenueChange * (revenue - previousRevenue);
            var freeCashFlow = ebit - tax + depreciation - capex - deltaNwc;
            var factor = DiscountFactor(scenario.Wacc, MidYearPeriod(year));
            years.Add(new ProjectedYear(year, revenue, ebit, depreciation, capex, deltaNwc, freeCashFlow, factor, freeCashFlow * factor));
            previousRevenue = revenue;
        }

        return years;
    }
}
