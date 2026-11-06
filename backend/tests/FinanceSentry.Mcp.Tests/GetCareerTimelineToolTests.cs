using System.ComponentModel;
using System.Reflection;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetCareerTimelineToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetCareerTimelineQuery, CareerTimelineResponse>> _handler = new();

    private GetCareerTimelineTool CreateSut(Guid? resolvedUserId = null) =>
        new(_handler.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId }, NullLogger<GetCareerTimelineTool>.Instance);

    private static MethodInfo Method => typeof(GetCareerTimelineTool).GetMethod(nameof(GetCareerTimelineTool.ExecuteAsync))!;

    [Fact]
    public async Task ExecuteAsync_ReturnsTheTimeline_ForTheAuthenticatedUser()
    {
        var timeline = new CareerTimelineResponse(false, [], 0);
        _handler
            .Setup(h => h.Handle(It.Is<GetCareerTimelineQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(timeline);

        (await CreateSut(UserId).ExecuteAsync()).Should().BeSameAs(timeline);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenThereIsNoIdentity()
    {
        (await CreateSut(resolvedUserId: null).ExecuteAsync()).Should().BeNull();
        _handler.Verify(h => h.Handle(It.IsAny<GetCareerTimelineQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenTheHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetCareerTimelineQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        (await CreateSut(UserId).ExecuteAsync()).Should().BeNull();
    }

    [Fact]
    public void TheTool_IsMarkedSensitiveProfile_SoEveryCallIsAudited()
    {
        var marker = Method.GetCustomAttribute<SensitiveProfileAttribute>();

        marker.Should().NotBeNull("a tool that returns career events must be on the audit trail");
        marker!.FieldNames.Should().Contain(["Label", "Amount", "Note"]);
    }

    [Fact]
    public void GoalsProgressTool_IsAlsoOnTheAuditTrail_BecauseItsScenarioIsDerivedFromCareerEvents()
        => typeof(GetGoalsProgressTool).GetMethod(nameof(GetGoalsProgressTool.ExecuteAsync))!
            .GetCustomAttribute<SensitiveProfileAttribute>().Should().NotBeNull();

    [Fact]
    public void Description_SaysScenarioNotForecast_AndWhatAKeylessHostSees()
    {
        var description = Method.GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.Should().Contain("not a forecast");
        description.Should().Contain("readable false");
        description.Should().Contain("never advice");
    }
}
