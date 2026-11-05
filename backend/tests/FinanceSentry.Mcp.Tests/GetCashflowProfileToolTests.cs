using System.ComponentModel;
using System.Reflection;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetCashflowProfileToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>> _handler = new();

    private GetCashflowProfileTool CreateSut(Guid? resolvedUserId = null) =>
        new(_handler.Object, new FakeIdentityResolver { ResolvedUserId = resolvedUserId }, NullLogger<GetCashflowProfileTool>.Instance);

    private static CashflowProfileResponse Shown() => new(
        CashflowProfileStatus.Shown, CompleteMonths: 6, FromMonth: "2026-03", ToMonth: "2026-08",
        IncomeMonthlyMedian: 4_000m, BurnMonthlyMedian: 3_000m, SavingsRate: 0.25m, RunwayMonths: 6.0m,
        RunwayReason: null, CommittedShare: 0.2m, HasStaleSleeves: false);

    [Fact]
    public async Task ExecuteAsync_ReturnsTheProfile_ForTheAuthenticatedUser()
    {
        _handler
            .Setup(h => h.Handle(It.Is<GetCashflowProfileQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shown());

        var result = await CreateSut(UserId).ExecuteAsync();

        result.Should().NotBeNull();
        result!.Status.Should().Be(CashflowProfileStatus.Shown);
        result.SavingsRate.Should().Be(0.25m);
        result.Disclosure.Should().Contain("never a trading signal");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenThereIsNoIdentity()
    {
        var result = await CreateSut(resolvedUserId: null).ExecuteAsync();

        result.Should().BeNull();
        _handler.Verify(
            h => h.Handle(It.IsAny<GetCashflowProfileQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenTheHandlerThrows()
    {
        _handler
            .Setup(h => h.Handle(It.IsAny<GetCashflowProfileQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await CreateSut(UserId).ExecuteAsync();

        result.Should().BeNull();
    }

    [Fact]
    public void Description_SaysTheFiguresAreContext_NeverATradingSignal()
    {
        var description = typeof(GetCashflowProfileTool)
            .GetMethod(nameof(GetCashflowProfileTool.ExecuteAsync))!
            .GetCustomAttribute<DescriptionAttribute>()!
            .Description;

        description.Should().Contain("never a trading signal");
    }
}
