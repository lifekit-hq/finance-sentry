using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Research.Application.Queries;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class CatalystToolsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<ListCatalystEventsQuery, CatalystEventDigest>> _events = new();
    private readonly Mock<IQueryHandler<GetThemeAttentionQuery, ThemeAttentionView>> _attention = new();

    [Fact]
    public async Task ListCatalystEvents_ReturnsAnEmptyDigest_WhenIdentityUnresolved()
    {
        var result = await new ListCatalystEventsTool(_events.Object, new FakeIdentityResolver { ResolvedUserId = null }).ExecuteAsync();

        result.Total.Should().Be(0);
        result.Events.Should().BeEmpty();
        _events.Verify(h => h.Handle(It.IsAny<ListCatalystEventsQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListCatalystEvents_QueriesForTheAuthenticatedUser_WithTheWindowAndLimit()
    {
        ListCatalystEventsQuery? captured = null;
        var digest = new CatalystEventDigest(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, false, []);
        _events.Setup(h => h.Handle(It.IsAny<ListCatalystEventsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ListCatalystEventsQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(digest);
        var since = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);

        var result = await new ListCatalystEventsTool(_events.Object, new FakeIdentityResolver { ResolvedUserId = UserId }).ExecuteAsync(since, 25);

        captured!.UserId.Should().Be(UserId);
        captured.Since.Should().Be(since);
        captured.Limit.Should().Be(25);
        result.Should().BeSameAs(digest);
    }

    [Fact]
    public async Task GetThemeAttention_PassesTheThemeThrough()
    {
        GetThemeAttentionQuery? captured = null;
        var view = new ThemeAttentionView("memory", 0, [], "none");
        _attention.Setup(h => h.Handle(It.IsAny<GetThemeAttentionQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetThemeAttentionQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(view);

        var result = await new GetThemeAttentionTool(_attention.Object).ExecuteAsync("memory");

        captured!.Theme.Should().Be("memory");
        result.Should().BeSameAs(view);
    }
}
