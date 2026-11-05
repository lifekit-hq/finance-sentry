namespace FinanceSentry.Modules.Research.Tests;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>#454 S2: the published goals port reads the current IPS through Research's own query and nothing else.</summary>
public sealed class GoalsReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static IpsDto Ips(IReadOnlyList<InvestmentGoal> goals, ContributionPlan? plan) => new(
        Guid.NewGuid(), Version: 3, IsCurrent: true, goals, PrimaryHorizonYears: 20, EmergencyCushionUsd: null,
        RiskTolerance: 3, RiskCapacity: null, MaxDrawdownTolerancePct: null, RiskMeasuredAt: null, AllocationTargets: [],
        RebalancingRule.Default, plan, SellDiscipline: null, CoolingOffDays: 90, Exclusions: [], ReviewCadence: "annual",
        LastReviewedAt: null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static GoalsReader ReaderOver(IpsDto? ips)
    {
        var get = new Mock<IQueryHandler<GetIpsQuery, IpsDto?>>();
        get.Setup(h => h.Handle(It.Is<GetIpsQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ips);
        return new GoalsReader(get.Object);
    }

    [Fact]
    public async Task GetCurrentAsync_NoCurrentIps_IsNull()
    {
        (await ReaderOver(null).GetCurrentAsync(UserId)).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentAsync_CarriesGoalsInOrder_AndTheContributionPlan()
    {
        var ips = Ips(
            [
                new InvestmentGoal("House deposit", 80_000m, new DateOnly(2030, 6, 1), "high"),
                new InvestmentGoal("Someday", null, null, "low"),
            ],
            new ContributionPlan(500m, "monthly"));

        var result = await ReaderOver(ips).GetCurrentAsync(UserId);

        result!.Goals.Should().HaveCount(2);
        result.Goals[0].Label.Should().Be("House deposit");
        result.Goals[0].TargetAmountUsd.Should().Be(80_000m);
        result.Goals[0].TargetDate.Should().Be(new DateOnly(2030, 6, 1));
        result.Goals[0].Priority.Should().Be("high");
        result.Goals[1].TargetAmountUsd.Should().BeNull();
        result.Goals[1].TargetDate.Should().BeNull();
        result.ContributionPlan!.AmountUsd.Should().Be(500m);
        result.ContributionPlan.Cadence.Should().Be("monthly");
    }

    [Fact]
    public async Task GetCurrentAsync_IpsWithoutPlanOrGoals_IsEmptyNotNull()
    {
        var result = await ReaderOver(Ips([], plan: null)).GetCurrentAsync(UserId);

        result.Should().NotBeNull("an IPS exists; it just lists no goal");
        result!.Goals.Should().BeEmpty();
        result.ContributionPlan.Should().BeNull();
    }
}
