using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class CoverageToolsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<ListCoverageQuery, CoverageListView>> _list = new();
    private readonly Mock<IQueryHandler<ExplainCoverageQuery, CoverageExplanation>> _explain = new();

    private readonly Mock<IQueryHandler<ListCoverageEventsQuery, CoverageEventDigest>> _events = new();

    private ListCoverageEventsTool CreateEvents(Guid? user) => new(_events.Object, new FakeIdentityResolver { ResolvedUserId = user });

    private ListCoverageTool CreateList(Guid? user) => new(_list.Object, new FakeIdentityResolver { ResolvedUserId = user });

    private ExplainCoverageTool CreateExplain(Guid? user) => new(_explain.Object, new FakeIdentityResolver { ResolvedUserId = user });

    [Fact]
    public async Task ListCoverage_ReturnsAnEmptyView_WhenIdentityUnresolved()
    {
        var result = await CreateList(null).ExecuteAsync();

        result.Count.Should().Be(0);
        result.Members.Should().BeEmpty();
        _list.Verify(h => h.Handle(It.IsAny<ListCoverageQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListCoverage_QueriesForTheAuthenticatedUser()
    {
        ListCoverageQuery? captured = null;
        var view = new CoverageListView(30, 0, false, [], []);
        _list.Setup(h => h.Handle(It.IsAny<ListCoverageQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ListCoverageQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(view);

        var result = await CreateList(UserId).ExecuteAsync();

        captured!.UserId.Should().Be(UserId);
        result.Should().BeSameAs(view);
    }

    [Fact]
    public async Task ExplainCoverage_ReturnsNull_WhenIdentityUnresolvedOrTickerBlank()
    {
        (await CreateExplain(null).ExecuteAsync("MU")).Should().BeNull();
        (await CreateExplain(UserId).ExecuteAsync("  ")).Should().BeNull();
        _explain.Verify(h => h.Handle(It.IsAny<ExplainCoverageQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExplainCoverage_QueriesTheTickerForTheAuthenticatedUser()
    {
        ExplainCoverageQuery? captured = null;
        var explanation = new CoverageExplanation(
            "MU", false, "Not covered.", null, false, null, null, 0, [], [], new CoverageCapView(30, 0, false), [], []);
        _explain.Setup(h => h.Handle(It.IsAny<ExplainCoverageQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ExplainCoverageQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(explanation);

        var result = await CreateExplain(UserId).ExecuteAsync("MU");

        captured.Should().Be(new ExplainCoverageQuery(UserId, "MU"));
        result.Should().BeSameAs(explanation);
    }

    [Fact]
    public async Task ListCoverageEvents_ReturnsAnEmptyDigest_WhenIdentityUnresolved()
    {
        var since = new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

        var result = await CreateEvents(null).ExecuteAsync(since);

        result.Since.Should().Be(since);
        result.Total.Should().Be(0);
        result.Events.Should().BeEmpty();
        _events.Verify(h => h.Handle(It.IsAny<ListCoverageEventsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListCoverageEvents_QueriesTheWindowAndLimitForTheAuthenticatedUser()
    {
        ListCoverageEventsQuery? captured = null;
        var since = new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);
        var digest = new CoverageEventDigest(since, since.AddDays(1), 0, 0, 0, false, [], []);
        _events.Setup(h => h.Handle(It.IsAny<ListCoverageEventsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ListCoverageEventsQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(digest);

        var result = await CreateEvents(UserId).ExecuteAsync(since, 25);

        captured.Should().Be(new ListCoverageEventsQuery(UserId, since, 25));
        result.Should().BeSameAs(digest);
    }
}
