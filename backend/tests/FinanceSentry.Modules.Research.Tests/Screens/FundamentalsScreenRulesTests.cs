namespace FinanceSentry.Modules.Research.Tests.Screens;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Screens;
using FluentAssertions;
using Xunit;

/// <summary>The fundamentals screen's bars on fixture EDGAR facts: each measure, its boundary, and what "not evaluable" means.</summary>
public sealed class FundamentalsScreenRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);
    private static readonly DateOnly YearEnd = new(2026, 3, 31);
    private static readonly DateOnly PriorYearEnd = new(2025, 3, 31);

    private static readonly FundamentalsScreenThresholds Bars = new(
        MinRevenueGrowth: 0.15m,
        MinOperatingCashFlowMargin: 0.15m,
        MaxCapexIntensity: 0.15m,
        MinStockholdersEquity: 0m,
        MaxFactAgeDays: 550);

    private static FundamentalFact Fy(string concept, decimal value, DateOnly end, string unit = "USD")
        => new("TEST", concept, concept, unit, value, end, "FY", end.Year, "10-K");

    /// <summary>Revenue 1000 (+20% on 833.3), cash flow margin 30%, capex intensity 5%, positive equity: clears every bar.</summary>
    private static List<FundamentalFact> Passing()
        =>
        [
            Fy("Revenue", 1200m, YearEnd),
            Fy("Revenue", 1000m, PriorYearEnd),
            Fy("OperatingCashFlow", 360m, YearEnd),
            Fy("Capex", 60m, YearEnd),
            Fy("StockholdersEquity", 500m, YearEnd),
        ];

    private static List<FundamentalFact> With(string concept, params FundamentalFact[] replacement)
        => [.. Passing().Where(f => f.Concept != concept), .. replacement];

    [Fact]
    public void AllFourMeasures_ClearingTheirBars_Pass()
    {
        var reading = FundamentalsScreenRules.Evaluate(Passing(), Bars, Today);

        reading.Passed.Should().BeTrue();
        reading.PeriodEnd.Should().Be(YearEnd);
        reading.RevenueGrowth.Should().Be(0.2m);
        reading.OperatingCashFlowMargin.Should().Be(0.3m);
        reading.CapexIntensity.Should().Be(0.05m);
        reading.StockholdersEquity.Should().Be(500m);
        reading.Failed.Should().BeEmpty();
        reading.NotEvaluable.Should().BeEmpty();
    }

    [Fact]
    public void RevenueGrowthBelowTheBar_Fails()
    {
        var facts = With("Revenue", Fy("Revenue", 1100m, YearEnd), Fy("Revenue", 1000m, PriorYearEnd));

        var reading = FundamentalsScreenRules.Evaluate(facts, Bars, Today);

        reading.Passed.Should().BeFalse();
        reading.Failed.Should().Equal(FundamentalsScreenRules.RevenueGrowthMeasure);
    }

    [Fact]
    public void CashFlowMarginBelowTheBar_Fails()
    {
        var reading = FundamentalsScreenRules.Evaluate(
            With("OperatingCashFlow", Fy("OperatingCashFlow", 120m, YearEnd)), Bars, Today);

        reading.Passed.Should().BeFalse();
        reading.Failed.Should().Equal(FundamentalsScreenRules.OperatingCashFlowMarginMeasure);
    }

    [Fact]
    public void CapexIntensityAboveTheBar_Fails()
    {
        var reading = FundamentalsScreenRules.Evaluate(With("Capex", Fy("Capex", 300m, YearEnd)), Bars, Today);

        reading.Passed.Should().BeFalse();
        reading.Failed.Should().Equal(FundamentalsScreenRules.CapexIntensityMeasure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void EquityAtOrBelowTheFloor_Fails(int equity)
    {
        var reading = FundamentalsScreenRules.Evaluate(
            With("StockholdersEquity", Fy("StockholdersEquity", equity, YearEnd)), Bars, Today);

        reading.Passed.Should().BeFalse();
        reading.Failed.Should().Equal(FundamentalsScreenRules.EquityMeasure);
    }

    [Fact]
    public void GrowthMarginAndCapexExactlyAtTheirBars_Pass_ButEquityAtTheFloorDoesNot()
    {
        var facts = new List<FundamentalFact>
        {
            Fy("Revenue", 1150m, YearEnd),
            Fy("Revenue", 1000m, PriorYearEnd),
            Fy("OperatingCashFlow", 172.5m, YearEnd),
            Fy("Capex", 172.5m, YearEnd),
            Fy("StockholdersEquity", 1m, YearEnd),
        };

        FundamentalsScreenRules.Evaluate(facts, Bars, Today).Passed.Should().BeTrue();
    }

    [Fact]
    public void CapexIsReadAsAMagnitude_SoASignedFilingStillScoresItsIntensity()
    {
        var reading = FundamentalsScreenRules.Evaluate(With("Capex", Fy("Capex", -60m, YearEnd)), Bars, Today);

        reading.CapexIntensity.Should().Be(0.05m);
        reading.Passed.Should().BeTrue();
    }

    [Fact]
    public void AMissingCapexLine_IsNotEvaluable_NeverReadAsNoCapex()
    {
        var reading = FundamentalsScreenRules.Evaluate([.. Passing().Where(f => f.Concept != "Capex")], Bars, Today);

        reading.Passed.Should().BeFalse();
        reading.CapexIntensity.Should().BeNull();
        reading.NotEvaluable.Should().Equal(FundamentalsScreenRules.CapexIntensityMeasure);
        reading.Failed.Should().BeEmpty();
    }

    [Fact]
    public void ACapexReportedInAnotherUnit_IsNotPairedWithTheRevenue()
    {
        var reading = FundamentalsScreenRules.Evaluate(With("Capex", Fy("Capex", 60m, YearEnd, "EUR")), Bars, Today);

        reading.Passed.Should().BeFalse();
        reading.NotEvaluable.Should().Equal(FundamentalsScreenRules.CapexIntensityMeasure);
    }

    [Fact]
    public void NoPriorYearRevenue_LeavesGrowthNotEvaluable()
    {
        var reading = FundamentalsScreenRules.Evaluate(
            With("Revenue", Fy("Revenue", 1200m, YearEnd)), Bars, Today);

        reading.Passed.Should().BeFalse();
        reading.NotEvaluable.Should().Equal(FundamentalsScreenRules.RevenueGrowthMeasure);
    }

    [Fact]
    public void QuarterlyFactsAlone_AreNoAnnualRevenue()
    {
        var quarterly = new FundamentalFact("TEST", "Revenue", "Revenue", "USD", 300m, YearEnd, "Q4", 2026, "10-Q");

        var reading = FundamentalsScreenRules.Evaluate([quarterly], Bars, Today);

        reading.Passed.Should().BeFalse();
        reading.NotEvaluable.Should().Equal(FundamentalsScreenRules.NoAnnualRevenue);
    }

    [Fact]
    public void AFiscalYearTooOld_IsStaleEvidence_NotAPass()
    {
        var reading = FundamentalsScreenRules.Evaluate(Passing(), Bars, YearEnd.AddDays(551));

        reading.Passed.Should().BeFalse();
        reading.NotEvaluable.Should().Equal(FundamentalsScreenRules.StaleFacts);
        reading.PeriodEnd.Should().Be(YearEnd);
    }

    [Fact]
    public void NoFacts_IsNotEvaluable()
    {
        var reading = FundamentalsScreenRules.Evaluate([], Bars, Today);

        reading.Passed.Should().BeFalse();
        reading.NotEvaluable.Should().Equal(FundamentalsScreenRules.NoAnnualRevenue);
    }
}
