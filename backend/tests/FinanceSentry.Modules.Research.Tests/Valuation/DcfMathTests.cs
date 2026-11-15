namespace FinanceSentry.Modules.Research.Tests.Valuation;

using FinanceSentry.Modules.Research.Domain.Valuation;
using FluentAssertions;
using Xunit;

/// <summary>
/// DCF math (#1269), synthetic numbers only. Worked example (two years, hand-checked): revenue 1,000 growing 10% then
/// 5%, 20% operating margin, 25% tax, D&amp;A 5% and capex 6% of revenue, working capital 10% of the revenue change,
/// WACC 10%, terminal growth 3%.
/// Year 1: revenue 1,100, EBIT 220, NOPAT 165, D&amp;A 55, capex 66, change in NWC 10 -> FCF 144.
/// Year 2: revenue 1,155, EBIT 231, NOPAT 173.25, D&amp;A 57.75, capex 69.3, change in NWC 5.5 -> FCF 156.2.
/// Mid-year factors 1.1^-0.5 and 1.1^-1.5; terminal value 156.2 x 1.03 / 0.07 = 2,298.37 discounted at the year-2 factor.
/// </summary>
public sealed class DcfMathTests
{
    private const decimal Tolerance = 0.01m;

    private static readonly DcfMath.Drivers WorkedDrivers = new(
        BaseRevenue: 1000m, OperatingMargin: 0.20m, TaxRate: 0.25m,
        DepreciationPct: 0.05m, CapexPct: 0.06m, NwcPctOfRevenueChange: 0.10m);

    private static DcfMath.Scenario Worked(decimal wacc = 0.10m, decimal terminalGrowth = 0.03m) => new(
        WorkedDrivers, [0.10m, 0.05m], wacc, terminalGrowth);

    [Fact]
    public void Worked_example_projects_each_year_and_values_the_enterprise()
    {
        var v = DcfMath.Value(Worked())!;

        v.Years.Select(y => y.Revenue).Should().Equal(1100m, 1155m);
        v.Years.Select(y => y.Ebit).Should().Equal(220m, 231m);
        v.Years.Select(y => y.Depreciation).Should().Equal(55m, 57.75m);
        v.Years.Select(y => y.Capex).Should().Equal(66m, 69.3m);
        v.Years.Select(y => y.DeltaNwc).Should().Equal(10m, 5.5m);
        v.Years.Select(y => y.FreeCashFlow).Should().Equal(144m, 156.2m);

        v.PresentValueOfCashFlows.Should().BeApproximately(272.69m, Tolerance);
        v.TerminalValue.Should().BeApproximately(2298.37m, Tolerance);
        v.PresentValueOfTerminal.Should().BeApproximately(1992.19m, Tolerance);
        v.EnterpriseValue.Should().BeApproximately(2264.88m, Tolerance);
        v.TerminalShare.Should().BeApproximately(0.8796m, 0.0001m);
        v.FinalYearEbitda.Should().Be(288.75m);
    }

    [Fact]
    public void Worked_example_bridges_to_equity_value_per_share()
    {
        var v = DcfMath.Value(Worked())!;

        // (2,264.88 - net debt 300) / 50 shares
        CompsMath.PerShareFromEnterpriseValue(v.EnterpriseValue, 300m, 50m)
            .Should().BeApproximately(39.30m, Tolerance);
    }

    [Fact]
    public void Discount_factors_are_mid_year()
    {
        DcfMath.MidYearPeriod(1).Should().Be(0.5m);
        DcfMath.MidYearPeriod(5).Should().Be(4.5m);
        DcfMath.DiscountFactor(0.10m, 0.5m).Should().BeApproximately(0.953463m, 0.000001m);
        DcfMath.DiscountFactor(0.10m, 1.5m).Should().BeApproximately(0.866784m, 0.000001m);

        var years = DcfMath.Value(Worked())!.Years;
        years[0].DiscountFactor.Should().BeApproximately(0.953463m, 0.000001m);
        years[1].DiscountFactor.Should().BeApproximately(0.866784m, 0.000001m);
    }

    [Theory]
    [InlineData(0.10, 0.10)]
    [InlineData(0.10, 0.12)]
    public void Terminal_growth_at_or_above_wacc_is_refused(double wacc, double growth)
        => DcfMath.Value(Worked((decimal)wacc, (decimal)growth)).Should().BeNull();

    [Fact]
    public void Net_cash_wacc_equals_the_cost_of_equity()
    {
        var ke = DcfMath.CostOfEquity(riskFree: 0.04m, beta: 1.2m, equityRiskPremium: 0.05m);

        ke.Should().Be(0.10m);
        DcfMath.Wacc(ke, afterTaxCostOfDebt: 0.03m, equityMarketValue: 1000m, netDebt: -200m).Should().Be(ke);
        DcfMath.Wacc(ke, 0.03m, 1000m, netDebt: 0m).Should().Be(ke);
    }

    [Fact]
    public void Wacc_weights_equity_and_net_debt_by_market_value()
    {
        // 75% equity at 10% + 25% net debt at 4% after tax = 8.5%
        DcfMath.Wacc(0.10m, 0.04m, equityMarketValue: 750m, netDebt: 250m).Should().Be(0.085m);
    }

    [Fact]
    public void Growth_fades_in_a_straight_line_to_the_terminal_rate()
    {
        DcfMath.FadingGrowthPath(0.20m, 0.04m, 5).Should().Equal(0.20m, 0.16m, 0.12m, 0.08m, 0.04m);
        DcfMath.FadingGrowthPath(0.20m, 0.04m, 1).Should().Equal(0.20m);
        DcfMath.ConstantGrowthPath(0.07m, 3).Should().Equal(0.07m, 0.07m, 0.07m);
    }

    [Fact]
    public void Axis_is_symmetric_around_the_base_value()
    {
        var axis = DcfMath.Axis(0.09m, 0.005m, 2);

        axis.Should().Equal(0.08m, 0.085m, 0.09m, 0.095m, 0.10m);
        axis[2].Should().Be(0.09m);
    }

    [Fact]
    public void Grid_centre_equals_the_headline_and_a_refused_cell_stays_null()
    {
        var scenario = new DcfMath.Scenario(WorkedDrivers, DcfMath.FadingGrowthPath(0.10m, 0.03m, 5), 0.10m, 0.03m);
        var headline = DcfMath.Value(scenario)!.EnterpriseValue;

        var grid = DcfMath.Grid(
            DcfMath.Axis(scenario.Wacc, 0.005m, 2),
            DcfMath.Axis(scenario.TerminalGrowth, 0.005m, 2),
            (w, g) => DcfMath.Value(scenario with
            {
                Wacc = w,
                TerminalGrowth = g,
                GrowthPath = DcfMath.FadingGrowthPath(0.10m, g, 5),
            })?.EnterpriseValue);

        grid.Should().HaveCount(5);
        grid.Should().OnlyContain(row => row.Length == 5);
        grid[2][2].Should().Be(headline);
        // wacc 9% x growth 4%: still valid; both axes move the value the expected way.
        grid[0][2].Should().BeGreaterThan(headline, "a lower WACC values the same cash flows higher");
        grid[4][2].Should().BeLessThan(headline);
        grid[2][4].Should().BeGreaterThan(headline, "a higher terminal growth values them higher");
        grid[2][0].Should().BeLessThan(headline);
    }

    [Fact]
    public void A_grid_cell_with_terminal_growth_at_wacc_is_null()
    {
        var scenario = Worked(wacc: 0.10m, terminalGrowth: 0.09m);

        var grid = DcfMath.Grid(
            DcfMath.Axis(scenario.Wacc, 0.005m, 2),
            DcfMath.Axis(scenario.TerminalGrowth, 0.005m, 2),
            (w, g) => DcfMath.Value(scenario with { Wacc = w, TerminalGrowth = g })?.EnterpriseValue);

        // wacc 9% (row 0) x growth 9.5% / 10% is refused; the centre (10% x 9%) is not.
        grid[0][3].Should().BeNull();
        grid[0][4].Should().BeNull();
        grid[2][2].Should().NotBeNull();
    }

    [Fact]
    public void Reverse_dcf_growth_reproduces_the_price_within_half_a_percent()
    {
        var scenario = new DcfMath.Scenario(WorkedDrivers, DcfMath.FadingGrowthPath(0.10m, 0.03m, 5), 0.10m, 0.03m);
        decimal? PerShare(DcfMath.Scenario s)
            => DcfMath.Value(s) is { } v ? CompsMath.PerShareFromEnterpriseValue(v.EnterpriseValue, 300m, 50m) : null;

        const decimal price = 52m;
        var growth = DcfMath.Solve(
            g => PerShare(scenario with { GrowthPath = DcfMath.ConstantGrowthPath(g, 5) }), price, -0.30m, 0.60m);

        growth.Should().NotBeNull();
        var reproduced = PerShare(scenario with { GrowthPath = DcfMath.ConstantGrowthPath(growth!.Value, 5) });
        reproduced.Should().BeApproximately(price, price * 0.005m);
    }

    [Fact]
    public void Solve_returns_null_when_the_price_is_outside_the_interval()
    {
        // a price no growth in the interval can reach, and one every growth in it exceeds
        DcfMath.Solve(g => 10m + 100m * g, 1_000_000m, -0.3m, 0.6m).Should().BeNull();
        DcfMath.Solve(g => 10m + 100m * g, -1_000_000m, -0.3m, 0.6m).Should().BeNull();
    }

    [Fact]
    public void Exit_multiple_replaces_the_gordon_terminal_with_final_ebitda_times_the_multiple()
    {
        // PV of cash flows 272.69 + 288.75 x 8 discounted two full years at 10%
        var ev = DcfMath.EnterpriseValueAtExitMultiple(Worked(), 8m);

        ev.Should().BeApproximately(2181.78m, Tolerance);
        CompsMath.PerShareFromEnterpriseValue(ev!.Value, 300m, 50m).Should().BeApproximately(37.64m, Tolerance);
    }

    [Fact]
    public void A_loss_making_year_pays_no_tax_and_an_empty_projection_has_no_value()
    {
        var loss = new DcfMath.Scenario(WorkedDrivers with { OperatingMargin = -0.10m }, [0.05m], 0.10m, 0.02m);

        DcfMath.Value(loss)!.Years[0].FreeCashFlow
            .Should().Be(-105m + 52.5m - 63m - 5m, "EBIT -105 untaxed, D&A 52.5, capex 63, change in NWC 5");
        DcfMath.Value(new DcfMath.Scenario(WorkedDrivers, [], 0.10m, 0.02m)).Should().BeNull();
        DcfMath.EnterpriseValueAtExitMultiple(new DcfMath.Scenario(WorkedDrivers, [], 0.10m, 0.02m), 8m).Should().BeNull();
    }
}
