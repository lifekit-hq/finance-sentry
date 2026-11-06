namespace FinanceSentry.Modules.Research.Tests;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>#454 S7: the published risk-profile port reads the current IPS and the review status through Research's own queries.</summary>
public sealed class RiskProfileReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Measured = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static IpsDto Ips(int? capacity, decimal? drawdown, DateTimeOffset? measuredAt) => new(
        Guid.NewGuid(), Version: 4, IsCurrent: true, [], PrimaryHorizonYears: 25, EmergencyCushionUsd: 12_000m,
        RiskTolerance: 4, RiskCapacity: capacity, MaxDrawdownTolerancePct: drawdown, RiskMeasuredAt: measuredAt,
        AllocationTargets: [], RebalancingRule.Default, ContributionPlan: null, SellDiscipline: null, CoolingOffDays: 90,
        Exclusions: [], ReviewCadence: "quarterly", LastReviewedAt: null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static RiskProfileReader ReaderOver(IpsDto? ips, bool due)
    {
        var get = new Mock<IQueryHandler<GetIpsQuery, IpsDto?>>();
        get.Setup(h => h.Handle(It.Is<GetIpsQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>())).ReturnsAsync(ips);
        var status = new Mock<IQueryHandler<GetPolicyReviewStatusQuery, PolicyReviewStatusDto>>();
        status.Setup(h => h.Handle(It.Is<GetPolicyReviewStatusQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyReviewStatusDto(
                ips is not null, "quarterly", true, null, null, false, false, 0, null, RiskRemeasurementDue: due));
        return new RiskProfileReader(get.Object, status.Object);
    }

    [Fact]
    public async Task GetCurrentAsync_NoCurrentIps_IsNull()
    {
        (await ReaderOver(null, due: false).GetCurrentAsync(UserId)).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentAsync_CarriesTheRiskFields_AndTheDueFlagFromTheReviewStatus()
    {
        var profile = await ReaderOver(Ips(capacity: 3, drawdown: 25m, measuredAt: Measured), due: true).GetCurrentAsync(UserId);

        profile.Should().NotBeNull();
        profile!.RiskTolerance.Should().Be(4);
        profile.RiskCapacity.Should().Be(3);
        profile.MaxDrawdownTolerancePct.Should().Be(25m);
        profile.RiskMeasuredAt.Should().Be(Measured);
        profile.RemeasurementDue.Should().BeTrue();
        profile.PrimaryHorizonYears.Should().Be(25);
        profile.EmergencyCushionUsd.Should().Be(12_000m);
    }

    [Fact]
    public async Task GetCurrentAsync_AnUnsetCapacityAndDrawdown_StayNull_NotZero()
    {
        var profile = await ReaderOver(Ips(null, null, null), due: false).GetCurrentAsync(UserId);

        profile!.RiskCapacity.Should().BeNull();
        profile.MaxDrawdownTolerancePct.Should().BeNull();
        profile.RiskMeasuredAt.Should().BeNull();
        profile.RemeasurementDue.Should().BeFalse();
    }
}
