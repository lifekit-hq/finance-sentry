namespace FinanceSentry.Tests.Unit.Core;

using FinanceSentry.Core.Utils;
using FluentAssertions;
using Xunit;

public class BudgetPaceTests
{
    [Fact]
    public void Calculate_OnDayOne_ElapsedFractionIsOneOverDaysInMonth()
    {
        var asOfDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 30m, limitUsd: 300m, asOfDate);

        result.ElapsedFraction.Should().Be(1m / 30m);
    }

    [Fact]
    public void Calculate_MidMonth_DividesThroughElapsedFraction()
    {
        // 15th of a 30-day month: elapsed = 0.5, so pace doubles the plain ratio and
        // projects double the current spend by month end.
        var asOfDate = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 150m, limitUsd: 300m, asOfDate);

        result.ElapsedFraction.Should().Be(0.5m);
        result.PaceRatio.Should().Be(1m);
        result.ProjectedMonthEndSpend.Should().Be(300m);
    }

    [Fact]
    public void Calculate_OnLastDayOfMonth_ElapsedFractionIsOne()
    {
        var asOfDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 300m, limitUsd: 300m, asOfDate);

        result.ElapsedFraction.Should().Be(1m);
        result.PaceRatio.Should().Be(1m);
        result.ProjectedMonthEndSpend.Should().Be(300m);
    }

    [Fact]
    public void Calculate_TwentyEightDayMonth_UsesTwentyEightAsTheDenominator()
    {
        var asOfDate = new DateTime(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 100m, limitUsd: 200m, asOfDate);

        result.ElapsedFraction.Should().Be(14m / 28m);
    }

    [Fact]
    public void Calculate_ThirtyDayMonth_UsesThirtyAsTheDenominator()
    {
        var asOfDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 100m, limitUsd: 200m, asOfDate);

        result.ElapsedFraction.Should().Be(15m / 30m);
    }

    [Fact]
    public void Calculate_ThirtyOneDayMonth_UsesThirtyOneAsTheDenominator()
    {
        var asOfDate = new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 100m, limitUsd: 200m, asOfDate);

        result.ElapsedFraction.Should().Be(16m / 31m);
    }

    [Fact]
    public void Calculate_PastMonthEvaluatedAsOfItsLastDay_ElapsedFractionIsOneAndPaceCollapsesToPlainRatio()
    {
        // A past month is "complete" by passing its own last day, not the actual current date —
        // the primitive has no notion of "today" beyond the asOfDate it is given.
        var asOfDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 250m, limitUsd: 200m, asOfDate);

        result.ElapsedFraction.Should().Be(1m);
        result.PaceRatio.Should().Be(1.25m);
    }

    [Fact]
    public void Calculate_ZeroSpend_YieldsZeroPaceAndZeroProjection()
    {
        var asOfDate = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 0m, limitUsd: 300m, asOfDate);

        result.PaceRatio.Should().Be(0m);
        result.ProjectedMonthEndSpend.Should().Be(0m);
    }

    [Fact]
    public void Calculate_ZeroLimit_IsGuardedToZeroPaceInsteadOfThrowingOrDividingByZero()
    {
        var asOfDate = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 150m, limitUsd: 0m, asOfDate);

        result.PaceRatio.Should().Be(0m);
    }

    [Fact]
    public void Calculate_NegativeLimit_IsGuardedToZeroPaceInsteadOfThrowing()
    {
        var asOfDate = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 150m, limitUsd: -50m, asOfDate);

        result.PaceRatio.Should().Be(0m);
    }

    [Fact]
    public void Calculate_PaceRatioEqualsProjectedMonthEndSpendOverLimit()
    {
        var asOfDate = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = BudgetPace.Calculate(spentUsd: 171m, limitUsd: 300m, asOfDate);

        result.PaceRatio.Should().Be(result.ProjectedMonthEndSpend / 300m);
    }
}
