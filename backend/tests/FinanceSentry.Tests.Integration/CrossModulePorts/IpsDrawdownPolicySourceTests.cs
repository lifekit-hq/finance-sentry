using FinanceSentry.Integration;
using FinanceSentry.Modules.Research.Domain.Ports;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Integration.CrossModulePorts;

/// <summary>
/// #700: the IPS holds the drawdown tolerance in whole percent and the risk layer compares in
/// fractions, so the adapter is the one place that translates - and refuses a figure that is not a
/// usable threshold.
/// </summary>
public sealed class IpsDrawdownPolicySourceTests
{
    private static IpsDrawdownPolicySource Source(decimal? pct, bool hasIps = true)
    {
        var reader = new Mock<IRiskToleranceReader>();
        reader.Setup(r => r.GetCurrentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasIps ? new IpsRiskTolerance(pct) : null);
        return new IpsDrawdownPolicySource(reader.Object);
    }

    [Theory]
    [InlineData(25.0, 0.25)]
    [InlineData(100.0, 1.0)]
    [InlineData(7.5, 0.075)]
    public async Task Translates_WholePercent_ToFraction(double pct, double expected)
    {
        (await Source((decimal)pct).GetMaxDrawdownAsync(Guid.NewGuid(), default)).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(101.0)]
    public async Task UnusableFigure_IsNoThreshold(double pct)
    {
        (await Source((decimal)pct).GetMaxDrawdownAsync(Guid.NewGuid(), default)).Should().BeNull();
    }

    [Fact]
    public async Task UnsetFigure_IsNoThreshold()
    {
        (await Source(null).GetMaxDrawdownAsync(Guid.NewGuid(), default)).Should().BeNull();
    }

    [Fact]
    public async Task NoPolicyStatement_IsNoThreshold()
    {
        (await Source(25m, hasIps: false).GetMaxDrawdownAsync(Guid.NewGuid(), default)).Should().BeNull();
    }
}
