using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Alerts.API.Responses;
using FinanceSentry.Modules.Alerts.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class ListActiveAlertsToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetAlertsQuery, AlertsPageResponse>> _handler = new();

    private ListActiveAlertsTool CreateSut() =>
        new(_handler.Object, new FakeIdentityResolver(), NullLogger<ListActiveAlertsTool>.Instance);

    private static AlertDto MakeDto(
        bool isRead = false,
        bool isResolved = false,
        string type = "LowBalance",
        string severity = "Warning") =>
        new(
            Guid.NewGuid(),
            type,
            severity,
            "Test title",
            "Test message",
            null,
            null,
            isRead,
            isResolved,
            DateTimeOffset.UtcNow,
            isResolved ? DateTimeOffset.UtcNow : null,
            1,
            DateTimeOffset.UtcNow);

    private static AlertsPageResponse PageOf(params AlertDto[] items) =>
        new(items, items.Length, 0, 1, 100, 1);

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetAlertsQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenNoAlertsExist()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetAlertsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PageOf());

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsOnlyFiredAlerts_ExcludingResolved()
    {
        var fired = MakeDto(isRead: false, isResolved: false);
        var resolved = MakeDto(isRead: false, isResolved: true);

        _handler
            .Setup(h => h.Handle(It.IsAny<GetAlertsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PageOf(fired, resolved));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(1);
        result[0].AlertId.Should().Be(fired.Id.ToString());
        result[0].Status.Should().Be("Fired");
    }

    [Fact]
    public async Task ExecuteAsync_MapsAllFields_Correctly()
    {
        var dto = MakeDto(type: "UnusualSpend", severity: "Error");

        _handler
            .Setup(h => h.Handle(It.IsAny<GetAlertsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PageOf(dto));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(1);
        var entry = result[0];
        entry.AlertId.Should().Be(dto.Id.ToString());
        entry.Type.Should().Be("UnusualSpend");
        entry.Severity.Should().Be("Error");
        entry.Title.Should().Be(dto.Title);
        entry.Message.Should().Be(dto.Message);
        entry.FiredAt.Should().Be(dto.CreatedAt);
        entry.Status.Should().Be("Fired");
    }

    [Fact]
    public async Task ExecuteAsync_PassesUnreadFilter_ToQueryHandler()
    {
        GetAlertsQuery? captured = null;
        _handler
            .Setup(h => h.Handle(It.IsAny<GetAlertsQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetAlertsQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(PageOf());

        await CreateSut().ExecuteAsync(UserId);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(UserId);
        captured.Filter.Should().Be("unread");
        captured.Page.Should().Be(1);
        captured.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task ExecuteAsync_FiltersOutAllResolved_WhenMixed()
    {
        var active1 = MakeDto(isResolved: false, type: "LowBalance");
        var active2 = MakeDto(isResolved: false, type: "SyncFailure");
        var resolved1 = MakeDto(isResolved: true, type: "LowBalance");
        var resolved2 = MakeDto(isResolved: true, type: "UnusualSpend");

        _handler
            .Setup(h => h.Handle(It.IsAny<GetAlertsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PageOf(active1, resolved1, active2, resolved2));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(2);
        result.Select(e => e.Type).Should().BeEquivalentTo(["LowBalance", "SyncFailure"]);
        result.Should().AllSatisfy(e => e.Status.Should().Be("Fired"));
    }
}
