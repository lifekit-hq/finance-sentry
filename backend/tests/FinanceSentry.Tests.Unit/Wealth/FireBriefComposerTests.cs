namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FluentAssertions;
using Xunit;

public sealed class FireBriefComposerTests
{
    private static FireProjectionResponse Projection(
        FireProjectionStatus status = FireProjectionStatus.Projected,
        decimal netWorth = 300_000m,
        decimal target = 1_200_000m,
        decimal savings = 2_000m,
        bool stale = false) =>
        new(status, target, netWorth, savings, AnnualSpend: 48_000m, SafeWithdrawalRate: 0.04m,
            RealAnnualReturn: 0.05m, ProjectedDate: new DateOnly(2041, 3, 1), MonthsToFire: 173m,
            HasStaleSleeves: stale);

    [Fact]
    public void Projected_ShowsGaugeDateAndEveryAssumption()
    {
        var brief = FireBriefComposer.Compose(Projection())!;

        brief.Headline.Should().Be("FIRE brief: projected March 2041");
        brief.Body.Should().Contain("[██░░░░░░░░] 25% of target");
        brief.Body.Should().Contain("Net worth $300k of a $1.2m target.");
        brief.Body.Should().Contain("About 14y 5m away at $2k/month saved.");
        brief.Body.Should().Contain("4% safe withdrawal rate");
        brief.Body.Should().Contain("5% real annual return");
        brief.Body.Should().Contain("$48k/year spend (12 × median monthly outflow)");
    }

    [Fact]
    public void AlreadyReached_SaysSo_AndFillsTheGauge()
    {
        var brief = FireBriefComposer.Compose(
            Projection(FireProjectionStatus.AlreadyReached, netWorth: 1_500_000m))!;

        brief.Headline.Should().Be("FIRE brief: target reached");
        brief.Body.Should().Contain("[██████████] 100% of target");
        brief.Body.Should().Contain("already covers the target");
    }

    [Fact]
    public void NotSaving_StatesTheTargetIsOutOfReach()
    {
        var brief = FireBriefComposer.Compose(
            Projection(FireProjectionStatus.NotSaving, savings: -300m))!;

        brief.Headline.Should().Be("FIRE brief: not saving enough to reach the target");
        brief.Body.Should().Contain("out of reach");
    }

    [Fact]
    public void InsufficientHistory_ComposesNothing()
        => FireBriefComposer.Compose(Projection(FireProjectionStatus.InsufficientHistory)).Should().BeNull();

    [Fact]
    public void StaleSleeves_AddANotice()
        => FireBriefComposer.Compose(Projection(stale: true))!.Body.Should().Contain("not synced recently");

    [Fact]
    public void FreshSleeves_AddNoNotice()
        => FireBriefComposer.Compose(Projection())!.Body.Should().NotContain("not synced recently");

    [Fact]
    public void Brief_StaysWithinTheTwelveLineMessageRule()
        => FireBriefComposer.Compose(Projection(stale: true))!.Body.Split('\n').Length.Should().BeLessThan(12);
}
