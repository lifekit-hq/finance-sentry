namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;

public class IncomeLensTests
{
    [Fact]
    public void MonthlyIncome_IsTheTotalAtTheWithdrawalRate_OverTwelveMonths()
    {
        // 300,000 × 4% = 12,000 a year = 1,000 a month.
        IncomeLens.MonthlyIncome(300_000m, ProjectionAssumptions.Default).Should().Be(1_000m);
    }

    [Fact]
    public void MonthlyIncome_UsesThePersonsWithdrawalRate_NotTheReturn()
    {
        // 300,000 × 3% = 9,000 a year = 750 a month, whatever the return assumption.
        IncomeLens.MonthlyIncome(300_000m, new ProjectionAssumptions(0.08m, 0.03m)).Should().Be(750m);
        IncomeLens.MonthlyIncome(300_000m, new ProjectionAssumptions(0m, 0.03m)).Should().Be(750m);
    }

    [Fact]
    public void MonthlyIncome_IsTheInverseOfTheFireTarget()
    {
        // The FIRE target for 24,000 a year at 4% is 600,000; that total pays back 2,000 a month.
        var assumptions = ProjectionAssumptions.Default;
        var target = Projection.FinancialIndependence(24_000m, 0m, 1_000m, assumptions).TargetUsd;

        IncomeLens.MonthlyIncome(target, assumptions).Should().Be(24_000m / 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5_000)]
    public void MonthlyIncome_ZeroOrNegativeTotal_HasNoIncome(decimal total)
    {
        IncomeLens.MonthlyIncome(total, ProjectionAssumptions.Default).Should().BeNull();
    }

    [Fact]
    public void MonthlyIncome_NonPositiveWithdrawalRate_HasNoIncome()
    {
        IncomeLens.MonthlyIncome(300_000m, new ProjectionAssumptions(0.05m, 0m)).Should().BeNull();
    }
}
