using System.ComponentModel;
using System.Reflection;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Measures;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetGoalsProgressToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>> _handler = new();

    private GetGoalsProgressTool CreateSut(Guid? resolvedUserId = null) =>
        new(_handler.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId }, NullLogger<GetGoalsProgressTool>.Instance);

    private static GoalsProgressResponse Shown() => new(
        GoalsProgressStatus.Shown, CurrentNetWorth: 100_000m, MonthlySavings: 1_000m, CompleteMonths: 6,
        [
            new GoalProgress(
                "House deposit", 130_000m, new DateOnly(2028, 9, 26), "high", MonthsLeft: 24,
                RequiredMonthlySaving: new ProjectionRange(700m, 800m, 900m),
                ProjectedAtTargetDate: new ProjectionRange(130_000m, 135_000m, 140_000m),
                AlreadyReached: false, GoalTrack.OnTrack),
        ],
        ContributionPlan: null, new ProjectionRange(0.02m, 0.05m, 0.08m), HasStaleSleeves: false);

    [Fact]
    public async Task ExecuteAsync_ReturnsTheProgress_ForTheAuthenticatedUser()
    {
        _handler
            .Setup(h => h.Handle(It.Is<GetGoalsProgressQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shown());

        var result = await CreateSut(UserId).ExecuteAsync();

        result.Should().NotBeNull();
        result!.Goals.Single().OnTrack.Should().Be(GoalTrack.OnTrack);
        result.Basis.Should().Contain("whole net worth");
        result.Disclosure.Should().Contain("Hypothetical, not a guarantee.");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenThereIsNoIdentity()
    {
        var result = await CreateSut(resolvedUserId: null).ExecuteAsync();

        result.Should().BeNull();
        _handler.Verify(
            h => h.Handle(It.IsAny<GetGoalsProgressQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenTheHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetGoalsProgressQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await CreateSut(UserId).ExecuteAsync();

        result.Should().BeNull();
    }

    [Fact]
    public void Description_SaysTheFiguresAreContext_AndGoalsAreNotEarmarked()
    {
        var description = typeof(GetGoalsProgressTool)
            .GetMethod(nameof(GetGoalsProgressTool.ExecuteAsync))!
            .GetCustomAttribute<DescriptionAttribute>()!
            .Description;

        description.Should().Contain("never a trading signal");
        description.Should().Contain("WHOLE net worth plus savings");
    }
}
