namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;

/// <summary>#454 S7: the capacity indicator is a pure rubric; each input caps the band and the lowest cap is the suggestion.</summary>
public class CapacityIndicatorTests
{
    private static CapacityInputs Strong() => new(
        RunwayMonths: 30m, BurnMonthlyUsd: 2_000m, IncomeMonthlyUsd: 5_000m, IncomeSpread: 0.1m,
        ObservedBelowDeclared: false, ShortestGoalMonths: 120);

    [Fact]
    public void EveryInputStrong_SuggestsTheTopBand_AndListsEveryInput()
    {
        var result = CapacityIndicator.Suggest(Strong());

        result.Status.Should().Be(CapacityStatus.Suggested);
        result.SuggestedBand.Should().Be(5);
        result.Factors.Select(f => f.Code).Should().Equal(
            CapacityIndicator.Runway, CapacityIndicator.SpendingToIncome, CapacityIndicator.IncomeSpreadCode, CapacityIndicator.GoalHorizon);
        result.Missing.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2.9, 1)]
    [InlineData(3, 2)]
    [InlineData(5.9, 2)]
    [InlineData(6, 3)]
    [InlineData(11.9, 3)]
    [InlineData(12, 4)]
    [InlineData(23.9, 4)]
    [InlineData(24, 5)]
    [InlineData(60, 5)]
    public void TheRunway_SetsTheBand_AtItsThresholds(double months, int band)
    {
        var result = CapacityIndicator.Suggest(Strong() with { RunwayMonths = (decimal)months });

        result.SuggestedBand.Should().Be(band);
        result.Factors.Single(f => f.Code == CapacityIndicator.Runway).CapBand.Should().Be(band);
    }

    [Theory]
    [InlineData(0.99, 5)]
    [InlineData(1.0, 2)]
    [InlineData(1.4, 2)]
    public void SpendingAtOrAboveIncome_CapsTheBandAtTwo(double ratio, int band)
    {
        var result = CapacityIndicator.Suggest(Strong() with { BurnMonthlyUsd = (decimal)ratio * 5_000m });

        result.SuggestedBand.Should().Be(band);
        result.Factors.Single(f => f.Code == CapacityIndicator.SpendingToIncome).Value.Should().Be((decimal)ratio);
    }

    [Fact]
    public void SpendingJustBelowIncome_IsNotCalledAtOrAboveIncome_WhateverTheRoundedRatioReads()
    {
        var result = CapacityIndicator.Suggest(Strong() with { BurnMonthlyUsd = 4_980m, IncomeMonthlyUsd = 5_000m });

        result.SuggestedBand.Should().Be(5);
        result.Factors.Single(f => f.Code == CapacityIndicator.SpendingToIncome).CapBand.Should().Be(CapacityIndicator.MaxBand);
    }

    [Theory]
    [InlineData(0.5, 5)]
    [InlineData(0.51, 4)]
    [InlineData(1.0, 4)]
    [InlineData(1.01, 3)]
    [InlineData(3.0, 3)]
    public void AnUnsteadyIncome_CapsTheBand(double spread, int band)
    {
        CapacityIndicator.Suggest(Strong() with { IncomeSpread = (decimal)spread }).SuggestedBand.Should().Be(band);
    }

    [Fact]
    public void IncomeBelowWhatWasDeclared_CapsTheBandAtFour()
    {
        var result = CapacityIndicator.Suggest(Strong() with { ObservedBelowDeclared = true });

        result.SuggestedBand.Should().Be(4);
        result.Factors.Should().Contain(f => f.Code == CapacityIndicator.IncomeBelowDeclared && f.CapBand == 4);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(11, 2)]
    [InlineData(12, 3)]
    [InlineData(35, 3)]
    [InlineData(36, 4)]
    [InlineData(59, 4)]
    [InlineData(60, 5)]
    public void TheNearestDatedGoal_CapsTheBand(int months, int band)
    {
        CapacityIndicator.Suggest(Strong() with { ShortestGoalMonths = months }).SuggestedBand.Should().Be(band);
    }

    [Fact]
    public void TheLowestCapWins_SoOneWeakInputHoldsTheSuggestionDown()
    {
        var result = CapacityIndicator.Suggest(Strong() with { RunwayMonths = 7m, ShortestGoalMonths = 8, IncomeSpread = 1.5m });

        result.SuggestedBand.Should().Be(2, "the goal due in 8 months caps at 2 whatever the runway and the income say");
        result.Factors.Min(f => f.CapBand).Should().Be(2);
    }

    [Fact]
    public void AnUnknownRunway_SuggestsNothing_AndSaysWhy()
    {
        var result = CapacityIndicator.Suggest(Strong() with { RunwayMonths = null });

        result.Status.Should().Be(CapacityStatus.Insufficient);
        result.SuggestedBand.Should().BeNull();
        result.Reason.Should().Be(CapacityIndicator.RunwayUnknown);
        result.Missing.Should().Contain(CapacityIndicator.RunwayUnknown);
        result.Factors.Should().BeEmpty("nothing is read when nothing can be suggested");
    }

    [Fact]
    public void InputsThatAreNotKnown_AreLeftOut_NotFilledIn()
    {
        var result = CapacityIndicator.Suggest(new CapacityInputs(18m, null, null, null, false, null));

        result.SuggestedBand.Should().Be(4);
        result.Factors.Should().ContainSingle(f => f.Code == CapacityIndicator.Runway);
        result.Missing.Should().Contain([CapacityIndicator.IncomeUnknown, CapacityIndicator.NoDatedGoal]);
    }

    [Fact]
    public void ZeroIncome_DoesNotDivide_ItJustLeavesTheSpendingRatioOut()
    {
        var result = CapacityIndicator.Suggest(Strong() with { IncomeMonthlyUsd = 0m });

        result.Factors.Should().NotContain(f => f.Code == CapacityIndicator.SpendingToIncome);
    }

    [Fact]
    public void TheBandIsAlwaysWithinTheScale()
    {
        foreach (var runway in new[] { 0m, 1m, 8m, 40m })
        foreach (var goal in new int?[] { null, 1, 24, 200 })
        {
            var band = CapacityIndicator.Suggest(Strong() with { RunwayMonths = runway, ShortestGoalMonths = goal }).SuggestedBand;
            band.Should().BeInRange(CapacityIndicator.MinBand, CapacityIndicator.MaxBand);
        }
    }

}
