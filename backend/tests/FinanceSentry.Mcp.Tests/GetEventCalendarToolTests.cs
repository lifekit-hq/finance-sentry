using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Events.API.Responses;
using FinanceSentry.Modules.Events.Application.Queries;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetEventCalendarToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetUpcomingEventsQuery, UpcomingEventsResult>> _upcoming = new();
    private readonly Mock<IQueryHandler<GetFiredEventsQuery, FiredEventsPageResponse>> _fired = new();

    private GetEventCalendarTool CreateTool(Guid? resolvedUserId = null)
        => new(_upcoming.Object, _fired.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId });

    private static FiredEventDto Fired(DateTimeOffset at) => new(
        Guid.NewGuid(), "NewsCluster", "Warning", "MU", "News cluster: MU", "m", at, false, null, null, "awaiting");

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenIdentityUnresolved()
    {
        var result = await CreateTool().ExecuteAsync();

        result.Should().BeNull();
        _upcoming.Verify(h => h.Handle(It.IsAny<GetUpcomingEventsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        _fired.Verify(h => h.Handle(It.IsAny<GetFiredEventsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ComposesBothQueries_ForTheIdentityAndWindow()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        GetUpcomingEventsQuery? upcomingQuery = null;
        GetFiredEventsQuery? firedQuery = null;
        _upcoming.Setup(h => h.Handle(It.IsAny<GetUpcomingEventsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetUpcomingEventsQuery, CancellationToken>((q, _) => upcomingQuery = q)
            .ReturnsAsync(new UpcomingEventsResult([], today, today.AddDays(14), [new EventSourceStatusDto("macro", "ok")]));
        _fired.Setup(h => h.Handle(It.IsAny<GetFiredEventsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetFiredEventsQuery, CancellationToken>((q, _) => firedQuery = q)
            .ReturnsAsync(new FiredEventsPageResponse([Fired(DateTimeOffset.UtcNow), Fired(DateTimeOffset.UtcNow.AddDays(-30))], 2, 1, 50, 1));

        var result = await CreateTool(UserId).ExecuteAsync(daysAhead: 14, daysBack: 7, kinds: ["macro"]);

        upcomingQuery!.UserId.Should().Be(UserId);
        upcomingQuery.From.Should().Be(today);
        upcomingQuery.To.Should().Be(today.AddDays(14));
        upcomingQuery.Kinds.Should().BeEquivalentTo(["macro"]);
        firedQuery!.UserId.Should().Be(UserId);
        firedQuery.PageSize.Should().Be(50);
        result!.Fired.Should().ContainSingle("the 30-day-old event is outside daysBack");
        result.Sources.Should().ContainSingle(s => s.Source == "macro");
    }

    [Fact]
    public async Task ExecuteAsync_ClampsLimitToThePageMaximum()
    {
        GetFiredEventsQuery? firedQuery = null;
        _upcoming.Setup(h => h.Handle(It.IsAny<GetUpcomingEventsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpcomingEventsResult([], default, default, []));
        _fired.Setup(h => h.Handle(It.IsAny<GetFiredEventsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetFiredEventsQuery, CancellationToken>((q, _) => firedQuery = q)
            .ReturnsAsync(new FiredEventsPageResponse([], 0, 1, 100, 0));

        await CreateTool(UserId).ExecuteAsync(limit: 5000);

        firedQuery!.PageSize.Should().Be(GetFiredEventsQueryHandler.MaxPageSize);
    }
}
