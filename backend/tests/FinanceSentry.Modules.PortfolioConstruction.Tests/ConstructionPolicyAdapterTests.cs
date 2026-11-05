namespace FinanceSentry.Modules.PortfolioConstruction.Tests;

using FinanceSentry.Integration;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FluentAssertions;
using Moq;
using Xunit;

public sealed class ConstructionPolicyAdapterTests
{
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IIpsAllocationReader> _allocation = new();
    private readonly Mock<IIpsExclusionsReader> _exclusions = new();
    private readonly Mock<IRiskLimitsReader> _limits = new();

    private ConstructionPolicyAdapter Adapter() => new(_allocation.Object, _exclusions.Object, _limits.Object);

    public ConstructionPolicyAdapterTests()
    {
        _exclusions.Setup(e => e.GetCurrentAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    [Fact]
    public async Task ColdStartUser_HasNoSleevesNoExclusionsAndNoLimits()
    {
        var policy = await Adapter().GetAsync(UserId, CancellationToken.None);

        policy.Sleeves.Should().BeNull("with no IPS the archetype's default sleeves apply");
        policy.Exclusions.Should().BeEmpty();
        policy.MaxPositionPct.Should().BeNull();
        policy.MinCashPct.Should().BeNull();
    }

    [Fact]
    public async Task IpsAndRiskRules_AreRead_WithRiskFractionsTurnedIntoPercentagePoints()
    {
        _allocation.Setup(a => a.GetCurrentAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpsAllocationPolicy([new IpsAllocationSleeve("Equities", 70m, 60m, 80m), new IpsAllocationSleeve("Cash", 30m, 0m, 0m)], 5m, 25m));
        _exclusions.Setup(e => e.GetCurrentAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(["TOBACCO"]);
        _limits.Setup(l => l.GetCurrentAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new RiskLimits(0.08m, 0.1m));

        var policy = await Adapter().GetAsync(UserId, CancellationToken.None);

        policy.Sleeves!.Select(s => (s.AssetClass, s.TargetPct)).Should().Equal(("Equities", 70m), ("Cash", 30m));
        policy.Exclusions.Should().Equal("TOBACCO");
        policy.MaxPositionPct.Should().Be(8m);
        policy.MinCashPct.Should().Be(10m);
    }

    [Fact]
    public async Task AnIpsWithNoTargets_FallsBackToTheArchetypeSleeves()
    {
        _allocation.Setup(a => a.GetCurrentAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpsAllocationPolicy([], 5m, 25m));

        var policy = await Adapter().GetAsync(UserId, CancellationToken.None);

        policy.Sleeves.Should().BeNull();
    }
}
