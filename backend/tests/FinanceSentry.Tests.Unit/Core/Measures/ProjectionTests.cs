namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;

public class ProjectionTests
{
    private static readonly ProjectionAssumptions Central5 = ProjectionAssumptions.Default;
    private static ProjectionAssumptions ZeroReturn(decimal swr = 0.04m) => new(0m, swr);

    // --- the assumption set ---------------------------------------------------------------------------------

    [Fact]
    public void Default_IsFivePercentRealReturnAndFourPercentWithdrawal()
    {
        ProjectionAssumptions.Default.RealAnnualReturn.Should().Be(0.05m);
        ProjectionAssumptions.Default.SafeWithdrawalRate.Should().Be(0.04m);
    }

    [Fact]
    public void RealAnnualReturns_AreCentralPlusOrMinusThreePoints()
    {
        Central5.RealAnnualReturns.Should().Be(new ProjectionRange(0.02m, 0.05m, 0.08m));
    }

    [Theory]
    [InlineData(0.01, 0.0, 0.04)]
    [InlineData(0.0, 0.0, 0.03)]
    public void RealAnnualReturns_LowBoundIsFlooredAtZero(double central, double low, double high)
    {
        new ProjectionAssumptions((decimal)central, 0.04m).RealAnnualReturns
            .Should().Be(new ProjectionRange((decimal)low, (decimal)central, (decimal)high));
    }

    [Fact]
    public void RealAnnualReturns_NegativeCentral_KeepsLowAtOrBelowCentral()
    {
        var range = new ProjectionAssumptions(-0.01m, 0.04m).RealAnnualReturns;

        range.Low.Should().BeLessThanOrEqualTo(range.Central);
        range.Central.Should().BeLessThanOrEqualTo(range.High);
    }

    [Fact]
    public void RangeOf_OrdersScenariosAroundTheCentralOne()
    {
        // Months to a target fall as the return rises, so the low-return scenario is the high bound.
        ProjectionRange.Of(atLowReturn: 200m, atCentralReturn: 150m, atHighReturn: 120m)
            .Should().Be(new ProjectionRange(120m, 150m, 200m));
    }

    // --- the savings path -----------------------------------------------------------------------------------

    [Fact]
    public void SavingsPath_FewerThanThreeCompleteMonths_IsNull()
    {
        Projection.SavingsPathOf([1_000m, 2_000m]).Should().BeNull();
        Projection.SavingsPathOf([]).Should().BeNull();
    }

    [Fact]
    public void SavingsPath_IsTheMedianNetOfTheCompleteMonths()
    {
        var path = Projection.SavingsPathOf([1_000m, 50_000m, 1_200m, 900m]);

        path.Should().Be(new SavingsPath(1_100m, 4));
    }

    [Fact]
    public void Median_EmptySample_Throws()
    {
        var act = () => Projection.Median([]);

        act.Should().Throw<ArgumentException>();
    }

    // --- value after n months -------------------------------------------------------------------------------

    [Fact]
    public void ValueAfter_ZeroReturn_IsCurrentPlusSavingsTimesMonths()
    {
        // A 0% central return floors the low bound at 0% too; only the 3% high bound compounds.
        var range = Projection.ValueAfter(12, 10_000m, 500m, ZeroReturn());

        range.Low.Should().Be(16_000m);
        range.Central.Should().Be(16_000m);
        range.High.Should().BeGreaterThan(16_000m);
    }

    [Fact]
    public void ValueAfter_IsOrderedLowCentralHigh()
    {
        var range = Projection.ValueAfter(12, 100_000m, 1_000m, Central5);

        range.Low.Should().BeLessThan(range.Central);
        range.Central.Should().BeLessThan(range.High);
    }

    [Fact]
    public void ValueAfter_MatchesAMonthByMonthSimulationAtEachReturn()
    {
        const decimal current = 100_000m;
        const decimal savings = 1_000m;
        var range = Projection.ValueAfter(12, current, savings, Central5);

        range.Low.Should().BeApproximately(Simulate(current, savings, 0.02m, 12), 0.01m);
        range.Central.Should().BeApproximately(Simulate(current, savings, 0.05m, 12), 0.01m);
        range.High.Should().BeApproximately(Simulate(current, savings, 0.08m, 12), 0.01m);
    }

    [Fact]
    public void ValueAfter_NegativeSavings_DrawsTheBalanceDown()
    {
        var range = Projection.ValueAfter(12, 10_000m, -500m, ZeroReturn());

        range.Central.Should().Be(4_000m);
    }

    // --- financial independence -----------------------------------------------------------------------------

    [Fact]
    public void FinancialIndependence_TargetIsAnnualSpendOverSafeWithdrawalRate()
    {
        var result = Projection.FinancialIndependence(40_000m, 0m, 1_000m, Central5);

        result.TargetUsd.Should().Be(1_000_000m);
    }

    [Fact]
    public void FinancialIndependence_ZeroSafeWithdrawalRate_IsGuardedToZeroTargetInsteadOfDividingByZero()
    {
        var result = Projection.FinancialIndependence(40_000m, 0m, 1_000m, new ProjectionAssumptions(0.05m, 0m));

        result.TargetUsd.Should().Be(0m);
    }

    [Fact]
    public void FinancialIndependence_ZeroCentralReturn_UsesTargetMinusNetWorthOverSavings()
    {
        // target = 40,000 / 0.04 = 1,000,000; needs 900,000 more at 1,000/month.
        var result = Projection.FinancialIndependence(40_000m, 100_000m, 1_000m, ZeroReturn());

        result.Status.Should().Be(FireReachability.Reachable);
        result.MonthsToTarget!.Value.Central.Should().Be(900m);
        // The 0% low bound is the central one; the 3% high bound gets there sooner.
        result.MonthsToTarget.Value.High.Should().Be(900m);
        result.MonthsToTarget.Value.Low.Should().BeLessThan(900m);
    }

    [Fact]
    public void FinancialIndependence_MonthsAreOrdered_HighReturnSoonest()
    {
        var result = Projection.FinancialIndependence(40_000m, 200_000m, 2_000m, Central5);
        var months = result.MonthsToTarget!.Value;

        months.Low.Should().BeLessThan(months.Central);
        months.Central.Should().BeLessThan(months.High);
    }

    [Fact]
    public void FinancialIndependence_MatchesAnIndependentMonthByMonthSimulationAtEachReturn()
    {
        // A different code path from the closed-form log solve, so agreement is a real cross-check. The simulation
        // advances in whole months, so it lands on the ceiling of the exact month count: at most one above it.
        const decimal annualSpend = 40_000m;
        const decimal savings = 2_000m;
        const decimal current = 200_000m;
        var assumptions = new ProjectionAssumptions(0.06m, 0.04m);

        var months = Projection.FinancialIndependence(annualSpend, current, savings, assumptions).MonthsToTarget!.Value;

        AssertCeiling(months.High, SimulateMonthsToTarget(1_000_000m, current, savings, 0.03m));
        AssertCeiling(months.Central, SimulateMonthsToTarget(1_000_000m, current, savings, 0.06m));
        AssertCeiling(months.Low, SimulateMonthsToTarget(1_000_000m, current, savings, 0.09m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void FinancialIndependence_NonPositiveSavings_IsNotSavingWithNoMonths(int savings)
    {
        var result = Projection.FinancialIndependence(40_000m, 100_000m, savings, Central5);

        result.Status.Should().Be(FireReachability.NotSaving);
        result.MonthsToTarget.Should().BeNull();
    }

    [Fact]
    public void FinancialIndependence_NetWorthAtTarget_IsAlreadyReachedWithZeroMonthsAcrossTheRange()
    {
        var result = Projection.FinancialIndependence(40_000m, 1_000_000m, 1_000m, Central5);

        result.Status.Should().Be(FireReachability.AlreadyReached);
        result.MonthsToTarget.Should().Be(new ProjectionRange(0m, 0m, 0m));
    }

    [Fact]
    public void FinancialIndependence_NetWorthAboveTarget_IsAlreadyReachedEvenWithoutSavings()
    {
        // Already there overrides "not saving": a retiree with no further contributions who has hit the number
        // should not be told they cannot reach it.
        var result = Projection.FinancialIndependence(40_000m, 1_200_000m, 0m, Central5);

        result.Status.Should().Be(FireReachability.AlreadyReached);
    }

    [Fact]
    public void FinancialIndependence_DeeplyNegativeNetWorth_FallsBackToTheZeroReturnFormula()
    {
        // NW·rm + c ≤ 0 has no logarithm; the fallback ignores compounding instead of returning NaN.
        var result = Projection.FinancialIndependence(40_000m, -10_000_000m, 1_000m, Central5);

        result.Status.Should().Be(FireReachability.Reachable);
        result.MonthsToTarget!.Value.Central.Should().Be(11_000m);
    }

    private static void AssertCeiling(decimal exactMonths, decimal simulatedMonths)
    {
        exactMonths.Should().BeApproximately(simulatedMonths, 1m);
        simulatedMonths.Should().BeGreaterThanOrEqualTo(exactMonths);
    }

    private static decimal MonthlyRate(decimal annual) => (decimal)(Math.Pow(1 + (double)annual, 1.0 / 12) - 1);

    private static decimal Simulate(decimal balance, decimal savings, decimal annual, int months)
    {
        var rate = MonthlyRate(annual);
        for (var i = 0; i < months; i++)
            balance = balance * (1 + rate) + savings;
        return balance;
    }

    private static decimal SimulateMonthsToTarget(decimal target, decimal balance, decimal savings, decimal annual)
    {
        var rate = MonthlyRate(annual);
        var months = 0;
        while (balance < target && months < 12_000)
        {
            balance = balance * (1 + rate) + savings;
            months++;
        }

        return months;
    }
}
