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
}
