namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;

/// <summary>finance-framework §3.2 rule 3 for one aggregate series: a newly linked account is neither growth nor a contribution.</summary>
public class ScopeLinksTests
{
    private static readonly DateOnly D0 = new(2026, 7, 1);

    private static DateOnly Day(int n) => D0.AddDays(n);

    private static ValuePoint Value(int day, decimal? usd) => new(Day(day), usd);

    [Fact]
    public void Bridge_WithoutLinks_KeepsTheSeriesAndTheFlows()
    {
        var deposit = new ClassifiedFlow(Day(1), FlowClass.ExternalInflow, 50m);

        var bridge = ScopeLinks.Bridge([Value(2, 160m), Value(0, 100m), Value(1, 150m)], [deposit], []);

        bridge.Values.Select(v => v.Date).Should().Equal(Day(0), Day(1), Day(2));
        bridge.Flows.Should().Equal(deposit);
        bridge.NewlyLinkedUsd.Should().Be(0m);
        bridge.ScopeChanged.Should().BeFalse();
        bridge.VanishedDays.Should().BeEmpty();
    }

    [Fact]
    public void Bridge_OpensOnTheFirstCloseThatHoldsValue()
    {
        var bridge = ScopeLinks.Bridge([Value(0, null), Value(1, 0m), Value(2, 100m), Value(3, 101m)], [], []);

        bridge.Opening.Should().Be(Value(2, 100m));
        bridge.Closing.Should().Be(Value(3, 101m));
    }

    [Fact]
    public void Bridge_TurnsALinkInsideTheWindowIntoOneZeroReturnStretch()
    {
        var deposit = new ClassifiedFlow(Day(3), FlowClass.ExternalInflow, 10m);

        var bridge = ScopeLinks.Bridge(
            [Value(0, 100m), Value(1, 102m), Value(2, 140m), Value(3, 162m), Value(4, 170m)], [deposit], [Day(2)]);

        // The link day's own close is ambiguous, so the stretch runs 1 -> 3: 162 - 102 - the 10 deposit.
        bridge.Values.Select(v => v.Date).Should().Equal(Day(0), Day(1), Day(3), Day(4));
        bridge.NewlyLinkedUsd.Should().Be(50m);
        bridge.Flows.Should().Contain(new ClassifiedFlow(Day(3), FlowClass.ExternalInflow, 50m));
        bridge.Links.Should().Equal(Day(2));
        bridge.ScopeChanged.Should().BeTrue();
        TimeWeightedReturn.Measure(bridge.Values, bridge.Flows).Return.Value.Should()
            .BeApproximately(1.02m * (170m / 162m) - 1m, 0.0000001m);
    }

    [Fact]
    public void Bridge_MovesTheOpeningPastALinkOnTheFirstClose()
    {
        var bridge = ScopeLinks.Bridge([Value(0, 100m), Value(1, 150m), Value(2, 153m)], [], [Day(0)]);

        bridge.Opening.Should().Be(Value(1, 150m));
        bridge.NewlyLinkedUsd.Should().Be(0m);
        bridge.ScopeChanged.Should().BeTrue();
    }

    [Fact]
    public void Bridge_MovesTheClosingBeforeALinkOnTheLastClose()
    {
        var bridge = ScopeLinks.Bridge([Value(0, 100m), Value(1, 101m), Value(2, 150m)], [], [Day(2)]);

        bridge.Closing.Should().Be(Value(1, 101m));
        bridge.ScopeChanged.Should().BeTrue();
    }

    [Fact]
    public void Bridge_IgnoresLinksOutsideTheWindow()
    {
        var bridge = ScopeLinks.Bridge([Value(5, 100m), Value(6, 101m)], [], [Day(1), Day(9)]);

        bridge.ScopeChanged.Should().BeFalse();
        bridge.Values.Should().HaveCount(2);
    }

    [Fact]
    public void Bridge_DropsAZeroCloseNoWithdrawalExplains()
    {
        var bridge = ScopeLinks.Bridge([Value(0, 100m), Value(1, 0m), Value(2, 103m)], [], []);

        bridge.Values.Select(v => v.Date).Should().Equal(Day(0), Day(2));
        bridge.VanishedDays.Should().Equal(Day(1));
    }

    [Fact]
    public void Bridge_KeepsAZeroCloseAWithdrawalExplains()
    {
        var withdrawal = new ClassifiedFlow(Day(1), FlowClass.ExternalOutflow, 100m);

        var bridge = ScopeLinks.Bridge([Value(0, 100m), Value(1, 0m)], [withdrawal], []);

        bridge.Values.Select(v => v.Date).Should().Equal(Day(0), Day(1));
        bridge.VanishedDays.Should().BeEmpty();
    }
}
