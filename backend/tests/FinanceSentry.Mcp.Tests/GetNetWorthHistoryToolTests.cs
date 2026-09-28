using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetNetWorthHistoryToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse>> _handler = new();

    private GetNetWorthHistoryTool CreateSut() =>
        new(_handler.Object, new FakeIdentityResolver(), NullLogger<GetNetWorthHistoryTool>.Instance);

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenNoSnapshots()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse([], false));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_MapsSnapshots_PreservingOrder()
    {
        var snap1 = new NetWorthSnapshotDto(new DateOnly(2024, 1, 31), 1000m, 500m, 200m, 1700m, "USD", null, false);
        var snap2 = new NetWorthSnapshotDto(new DateOnly(2024, 2, 29), 1100m, 600m, 250m, 1950m, "USD", "brokerage", true);

        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthHistoryResponse([snap1, snap2], true));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().HaveCount(2);
        result[0].SnapshotDate.Should().Be(snap1.SnapshotDate);
        result[0].BankingTotal.Should().Be(1000m);
        result[0].BrokerageTotal.Should().Be(500m);
        result[0].CryptoTotal.Should().Be(200m);
        result[0].TotalNetWorth.Should().Be(1700m);
        result[0].Currency.Should().Be("USD");
        result[0].StaleSleeves.Should().BeNull();
        result[0].IsApproximate.Should().BeFalse();
        result[1].TotalNetWorth.Should().Be(1950m);
        result[1].StaleSleeves.Should().Be("brokerage");
        result[1].IsApproximate.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_PassesDateBounds_ToQueryHandler()
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 6, 30);

        GetNetWorthHistoryQuery? captured = null;
        _handler
            .Setup(h => h.Handle(It.IsAny<GetNetWorthHistoryQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetNetWorthHistoryQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new NetWorthHistoryResponse([], false));

        await CreateSut().ExecuteAsync(UserId, from, to);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(UserId);
        captured.From.Should().Be(from);
        captured.To.Should().Be(to);
    }
}
