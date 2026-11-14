using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.PortfolioConstruction.API.Responses;
using FinanceSentry.Modules.PortfolioConstruction.Application.Queries;
using FinanceSentry.Modules.PortfolioConstruction.Application.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class PortfolioGapToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetGapModeQuery, GapModeResult>> _handler = new();

    private GetPortfolioGapTool Create(Guid? user) => new(_handler.Object, new FakeIdentityResolver { ResolvedUserId = user });

    [Fact]
    public async Task GetPortfolioGap_ReturnsNull_WhenIdentityUnresolved()
    {
        var result = await Create(null).ExecuteAsync("growth");

        result.Should().BeNull();
        _handler.Verify(h => h.Handle(It.IsAny<GetGapModeQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPortfolioGap_QueriesTheArchetypeForTheAuthenticatedUser_AndReturnsTheLabelledGap()
    {
        GetGapModeQuery? captured = null;
        var gap = new GapModeResult(
            "balanced", DateTimeOffset.UnixEpoch, GapModeCalculator.InformationalLabel, true, 10000m, false, [], null,
            new GapSummaryDto("Missing: AAA.", ["AAA"], [], [], 0), [], [], [], []);
        _handler.Setup(h => h.Handle(It.IsAny<GetGapModeQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetGapModeQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(gap);

        var result = await Create(UserId).ExecuteAsync("balanced");

        captured.Should().Be(new GetGapModeQuery(UserId, "balanced"));
        result.Should().BeSameAs(gap);
        result!.Label.Should().Contain("not a recommendation to buy or sell");
    }

    [Fact]
    public async Task GetPortfolioGap_WithoutAnArchetype_AsksForTheConfiguredDefault()
    {
        _handler.Setup(h => h.Handle(It.IsAny<GetGapModeQuery>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<GapModeResult>(null!));

        await Create(UserId).ExecuteAsync();

        _handler.Verify(h => h.Handle(new GetGapModeQuery(UserId, null), It.IsAny<CancellationToken>()), Times.Once);
    }
}
