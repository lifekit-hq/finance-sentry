namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;

public class GrowthIndexTests
{
    [Fact]
    public void NoSteps_HasNoDrawdown() => GrowthIndex.Drawdown([]).Should().BeNull();

    [Fact]
    public void MaxIsTheDeepestFall_CurrentIsTheFallAtTheLastStep()
    {
        // Index: 1.10, 0.88 (a 20% fall from 1.10), 0.968 (still 12% below the 1.10 peak).
        var drawdown = GrowthIndex.Drawdown([0.10m, -0.20m, 0.10m])!.Value;

        drawdown.Max.Should().BeApproximately(0.2m, 0.000000001m);
        drawdown.Current.Should().BeApproximately(0.12m, 0.000000001m);
    }

    [Fact]
    public void ARisingIndexNeverFell()
        => GrowthIndex.Drawdown([0.01m, 0.02m]).Should().Be(new IndexDrawdown(0m, 0m));
}
