using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetFamilyClearingStatementToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetFamilyClearingStatementQuery, FamilyClearingStatement>> _handler = new();

    private static readonly FamilyClearingStatement SampleStatement = new(
        "2024-05",
        [new CounterpartyStatementLine("Mom", "family_support", 500m, 100m, 400m, [])],
        SupportTotalUsd: 100m,
        ReceivedTotalUsd: 500m,
        ExcludedRoutingLegs: 1);

    private GetFamilyClearingStatementTool CreateSut(FakeIdentityResolver? identity = null) =>
        new(_handler.Object, identity ?? new FakeIdentityResolver(), NullLogger<GetFamilyClearingStatementTool>.Instance);

    [Fact]
    public async Task ExecuteAsync_ReturnsEmptyStatement_WhenHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetFamilyClearingStatementQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("classification unavailable"));

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Counterparties.Should().BeEmpty();
        result.SupportTotalUsd.Should().Be(0m);
        result.ReceivedTotalUsd.Should().Be(0m);
        result.ExcludedRoutingLegs.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmptyStatement_WhenNoUserIdAndNoAuthenticatedIdentity()
    {
        var result = await CreateSut(new FakeIdentityResolver { ResolvedUserId = null }).ExecuteAsync(null);

        result.Counterparties.Should().BeEmpty();
        _handler.Verify(
            h => h.Handle(It.IsAny<GetFamilyClearingStatementQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsUserId_ToAuthenticatedIdentity_WhenOmitted()
    {
        GetFamilyClearingStatementQuery? captured = null;
        _handler
            .Setup(h => h.Handle(It.IsAny<GetFamilyClearingStatementQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetFamilyClearingStatementQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(SampleStatement);

        var identity = new FakeIdentityResolver { ResolvedUserId = UserId };
        var result = await CreateSut(identity).ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(UserId);
        result.Should().Be(SampleStatement);
    }

    [Fact]
    public async Task ExecuteAsync_PassesExplicitMonthAndMonths_ToTheQuery()
    {
        GetFamilyClearingStatementQuery? captured = null;
        _handler
            .Setup(h => h.Handle(It.IsAny<GetFamilyClearingStatementQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetFamilyClearingStatementQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(SampleStatement);

        await CreateSut().ExecuteAsync(UserId, month: "2024-03", months: 12);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(UserId);
        captured.Month.Should().Be("2024-03");
        captured.Months.Should().Be(12);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsHandlerResult_OnSuccess()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetFamilyClearingStatementQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleStatement);

        var result = await CreateSut().ExecuteAsync(UserId);

        result.Should().Be(SampleStatement);
    }
}
