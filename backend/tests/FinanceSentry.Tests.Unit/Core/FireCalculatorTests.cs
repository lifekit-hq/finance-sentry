namespace FinanceSentry.Tests.Unit.Core;

using FinanceSentry.Core.Utils;
using FluentAssertions;
using Xunit;

public class FireCalculatorTests
{
    [Fact]
    public void Calculate_TargetIsAnnualSpendOverSafeWithdrawalRate()
    {
        var result = FireCalculator.Calculate(
            annualSpend: 40_000m, safeWithdrawalRate: 0.04m,
            monthlySavings: 1_000m, currentNetWorth: 0m, realAnnualReturn: 0.05m);

        result.Target.Should().Be(1_000_000m);
    }

    [Fact]
    public void Calculate_ZeroSafeWithdrawalRate_IsGuardedToZeroTargetInsteadOfDividingByZero()
    {
        var result = FireCalculator.Calculate(
            annualSpend: 40_000m, safeWithdrawalRate: 0m,
            monthlySavings: 1_000m, currentNetWorth: 0m, realAnnualReturn: 0.05m);

        result.Target.Should().Be(0m);
    }

    [Fact]
    public void Calculate_ZeroRealReturn_UsesTheClosedForm_TargetMinusNetWorthOverSavings()
    {
        // target = 40,000 / 0.04 = 1,000,000; needs 900,000 more at 1,000/month.
        var result = FireCalculator.Calculate(
            annualSpend: 40_000m, safeWithdrawalRate: 0.04m,
            monthlySavings: 1_000m, currentNetWorth: 100_000m, realAnnualReturn: 0m);

        result.Status.Should().Be(FireReachability.Reachable);
        result.MonthsToFire.Should().Be(900m);
    }

    [Fact]
    public void Calculate_PositiveReturn_MatchesAnIndependentMonthBySimulation()
    {
        // Independently computed: simulate month-by-month compounding at the same monthly
        // rate the calculator derives, and find the month the balance crosses the target.
        // This is a different code path from the closed-form log solve, so agreement between
        // the two is a real cross-check rather than the same formula written twice.
        const decimal annualSpend = 40_000m;
        const decimal swr = 0.04m;
        const decimal monthlySavings = 2_000m;
        const decimal currentNetWorth = 200_000m;
        const decimal realAnnualReturn = 0.06m;

        var target = annualSpend / swr;
        var monthlyReturn = (decimal)(Math.Pow(1 + (double)realAnnualReturn, 1.0 / 12) - 1);

        var simulatedMonths = SimulateMonthsToTarget(target, currentNetWorth, monthlySavings, monthlyReturn);

        var result = FireCalculator.Calculate(annualSpend, swr, monthlySavings, currentNetWorth, realAnnualReturn);

        result.Status.Should().Be(FireReachability.Reachable);
        result.MonthsToFire.Should().NotBeNull();
        // The simulation advances in whole months, so it always lands on the ceiling of the
        // calculator's exact (real-valued) month count — at most one month above it.
        result.MonthsToFire!.Value.Should().BeApproximately(simulatedMonths, 1m);
        simulatedMonths.Should().BeGreaterThanOrEqualTo(result.MonthsToFire.Value);
    }

    [Fact]
    public void Calculate_NonPositiveSavings_IsNotSavingWithNoMonthsToFire()
    {
        var result = FireCalculator.Calculate(
            annualSpend: 40_000m, safeWithdrawalRate: 0.04m,
            monthlySavings: 0m, currentNetWorth: 100_000m, realAnnualReturn: 0.05m);

        result.Status.Should().Be(FireReachability.NotSaving);
        result.MonthsToFire.Should().BeNull();
    }

    [Fact]
    public void Calculate_NegativeSavings_IsNotSavingWithNoMonthsToFire()
    {
        var result = FireCalculator.Calculate(
            annualSpend: 40_000m, safeWithdrawalRate: 0.04m,
            monthlySavings: -500m, currentNetWorth: 100_000m, realAnnualReturn: 0.05m);

        result.Status.Should().Be(FireReachability.NotSaving);
        result.MonthsToFire.Should().BeNull();
    }

    [Fact]
    public void Calculate_NetWorthAtOrAboveTarget_IsAlreadyReachedWithZeroMonths()
    {
        var result = FireCalculator.Calculate(
            annualSpend: 40_000m, safeWithdrawalRate: 0.04m,
            monthlySavings: 1_000m, currentNetWorth: 1_000_000m, realAnnualReturn: 0.05m);

        result.Status.Should().Be(FireReachability.AlreadyReached);
        result.MonthsToFire.Should().Be(0m);
    }

    [Fact]
    public void Calculate_NetWorthAboveTarget_IsAlreadyReachedEvenWithoutSavings()
    {
        // Already there overrides "not saving" — a retiree with no further contributions who
        // has already hit the number should not be told they cannot reach it.
        var result = FireCalculator.Calculate(
            annualSpend: 40_000m, safeWithdrawalRate: 0.04m,
            monthlySavings: 0m, currentNetWorth: 1_200_000m, realAnnualReturn: 0.05m);

        result.Status.Should().Be(FireReachability.AlreadyReached);
        result.MonthsToFire.Should().Be(0m);
    }

    private static decimal SimulateMonthsToTarget(
        decimal target, decimal startingBalance, decimal monthlySavings, decimal monthlyReturn)
    {
        var balance = startingBalance;
        var months = 0;
        while (balance < target && months < 12_000)
        {
            balance = balance * (1 + monthlyReturn) + monthlySavings;
            months++;
        }

        return months;
    }
}
