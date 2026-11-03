namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FluentAssertions;
using Xunit;

public sealed class JulyNetWorthHistoryRepairPlanTests
{
    private static readonly DateOnly[] RederivedDates =
    [
        new(2026, 7, 2), new(2026, 7, 3), new(2026, 7, 11),
        new(2026, 7, 14), new(2026, 7, 15), new(2026, 7, 16), new(2026, 7, 17),
        new(2026, 7, 18), new(2026, 7, 19), new(2026, 7, 20),
    ];

    [Fact]
    public void Plan_CoversEveryDayFromJuly1ToAugust30Once()
    {
        var days = JulyNetWorthHistoryRepairPlan.Days;

        days.Should().HaveCount(61);
        days.Select(d => d.Date).Should().OnlyHaveUniqueItems();
        days.Min(d => d.Date).Should().Be(new DateOnly(2026, 7, 1));
        days.Max(d => d.Date).Should().Be(new DateOnly(2026, 8, 30));
    }

    [Fact]
    public void Plan_RederivesBrokerageOnExactlyTheEpisodeDays()
    {
        JulyNetWorthHistoryRepairPlan.Days.Where(d => d.BrokerageRederived).Select(d => d.Date)
            .Should().BeEquivalentTo(RederivedDates);
    }

    [Fact]
    public void Plan_CreditLimitDeltaIsNinetyThousandHryvniaAtAPlausibleJulyAugustRate()
    {
        // 90,000 UAH at 44.4-44.9 UAH/USD (NBU, July-August 2026) is about 2,005-2,027 USD.
        foreach (var day in JulyNetWorthHistoryRepairPlan.Days)
        {
            var rate = JulyNetWorthHistoryRepairPlan.MonobankCreditLimitUah / -day.BankingDelta;
            rate.Should().BeInRange(44.3m, 45.0m, because: day.Date.ToString("O"));
        }
    }

    [Fact]
    public void Plan_OnADayWithoutABrokerageEpisode_EveryDollarOfChangeIsTheBankingCorrection()
    {
        foreach (var day in JulyNetWorthHistoryRepairPlan.Days.Where(d => !d.BrokerageRederived))
            day.BrokerageDelta.Should().Be(0m, because: day.Date.ToString("O"));
    }

    [Fact]
    public void Plan_OnABrokerageEpisodeDay_TheBrokerageMovesByTheRestOfTheChange()
    {
        var day = JulyNetWorthHistoryRepairPlan.Days.Single(d => d.Date == new DateOnly(2026, 7, 17));

        // Stored 15,062.70 -> proposed 11,692.28: -3,370.42 in all, of which -2,017.15 is the credit limit.
        day.BrokerageDelta.Should().Be(-1353.27m);
    }

    private static NetWorthSnapshot Row(decimal banking, decimal brokerage, decimal crypto, string? stale = null, FxStatus? fx = FxStatus.Exact) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        SnapshotDate = new DateOnly(2026, 7, 17),
        BankingTotal = banking,
        BrokerageTotal = brokerage,
        CryptoTotal = crypto,
        TotalNetWorth = banking + brokerage + crypto,
        CashTotal = banking + 100m,
        BrokerageInvested = brokerage - 100m,
        CryptoInvested = crypto,
        StaleSleeves = stale,
        FxStatus = fx,
    };

    [Fact]
    public void Repair_LandsExactlyOnTheProposedTotalAndKeepsCashPlusInvestedEqualToIt()
    {
        var day = new NetWorthRepairDay(new DateOnly(2026, 7, 17), 15_062.70m, 11_692.28m, -2_017.15m, BrokerageRederived: true);
        var row = Row(banking: 4_000m, brokerage: 9_912.27m, crypto: 1_150.43m);

        var repaired = JulyNetWorthHistoryRepairPlan.Repair(row, day);

        repaired.Total.Should().Be(11_692.28m);
        (repaired.Banking + repaired.Brokerage + 1_150.43m).Should().Be(repaired.Total);
        (repaired.Cash + repaired.BrokerageInvested + 1_150.43m).Should().Be(repaired.Total);
        repaired.Banking.Should().Be(4_000m - 2_017.15m);
        repaired.Brokerage.Should().Be(9_912.27m - 1_353.27m);
    }

    [Fact]
    public void Repair_OfABrokerageEpisodeDay_FlagsTheDayApproximateStaleAndTodayRate()
    {
        var day = new NetWorthRepairDay(new DateOnly(2026, 7, 17), 100m, 80m, -10m, BrokerageRederived: true);

        var repaired = JulyNetWorthHistoryRepairPlan.Repair(Row(50m, 50m, 0m, stale: "banking"), day);

        repaired.StaleSleeves.Should().Be("banking,brokerage");
        repaired.IsApproximate.Should().BeTrue();
        repaired.Fx.Should().Be(FxStatus.TodayRate);
    }

    [Fact]
    public void Repair_OfACreditLimitOnlyDay_LeavesFlagsAndBrokerageAlone()
    {
        var day = new NetWorthRepairDay(new DateOnly(2026, 7, 17), 100m, 90m, -10m, BrokerageRederived: false);

        var repaired = JulyNetWorthHistoryRepairPlan.Repair(Row(50m, 50m, 0m, fx: FxStatus.NearestPublished), day);

        repaired.Brokerage.Should().Be(50m);
        repaired.Banking.Should().Be(40m);
        repaired.StaleSleeves.Should().BeNull();
        repaired.IsApproximate.Should().BeFalse();
        repaired.Fx.Should().Be(FxStatus.NearestPublished);
    }

    [Fact]
    public void Repair_LeavesANullCashSplitNull()
    {
        var day = new NetWorthRepairDay(new DateOnly(2026, 7, 17), 100m, 90m, -10m, BrokerageRederived: false);
        var row = new NetWorthSnapshot { BankingTotal = 100m, TotalNetWorth = 100m };

        var repaired = JulyNetWorthHistoryRepairPlan.Repair(row, day);

        repaired.Cash.Should().BeNull();
        repaired.BrokerageInvested.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "brokerage")]
    [InlineData("", "brokerage")]
    [InlineData("brokerage", "brokerage")]
    [InlineData("crypto", "brokerage,crypto")]
    [InlineData("crypto,banking", "banking,brokerage,crypto")]
    public void WithStale_AddsTheSleeveOnceInSleeveOrder(string? stale, string expected)
        => JulyNetWorthHistoryRepairPlan.WithStale(stale, "brokerage").Should().Be(expected);
}
