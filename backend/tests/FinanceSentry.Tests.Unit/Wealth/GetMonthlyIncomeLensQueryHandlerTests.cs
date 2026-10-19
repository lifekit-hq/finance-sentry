namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetMonthlyIncomeLensQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<INetWorthSnapshotRepository> _snapshots = new();
    private readonly Mock<IHonestMonthlyFlowReader> _monthlyFlow = new();
    private readonly Mock<IUserFireAssumptionsReader> _assumptions = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private GetMonthlyIncomeLensQueryHandler CreateHandler() =>
        new(new ProjectionInputsReader(_snapshots.Object, _monthlyFlow.Object, _assumptions.Object, _clock));

    private void Arrange(decimal? netWorth, FireAssumptions? assumptions = null)
    {
        _snapshots.Setup(r => r.GetLatestByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(netWorth is { } value
                ? new NetWorthSnapshot
                {
                    Id = Guid.NewGuid(),
                    UserId = UserId,
                    SnapshotDate = new DateOnly(2026, 9, 20),
                    TotalNetWorth = value,
                    TakenAt = Now,
                }
                : null);
        // No complete months: the lens needs only the total and the withdrawal rate, never the savings path.
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, Projection.LookbackMonths, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _assumptions.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(assumptions);
    }

    [Fact]
    public async Task Handle_ShowsTheNetWorthAtTheDefaultWithdrawalRate_EvenWithoutSavingsHistory()
    {
        Arrange(300_000m);

        var result = await CreateHandler().Handle(new GetMonthlyIncomeLensQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(MonthlyIncomeLensStatus.Shown);
        result.NetWorth.Should().Be(300_000m);
        result.SafeWithdrawalRate.Should().Be(0.04m);
        result.MonthlyIncome.Should().Be(1_000m);
        result.BaseCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_UsesThePersonsWithdrawalRate_TheSameOneTheFireTargetUses()
    {
        Arrange(300_000m, new FireAssumptions(SafeWithdrawalRate: 0.03m, RealAnnualReturn: 0.07m));

        var result = await CreateHandler().Handle(new GetMonthlyIncomeLensQuery(UserId), CancellationToken.None);

        result.SafeWithdrawalRate.Should().Be(0.03m);
        result.MonthlyIncome.Should().Be(750m);
    }

    [Fact]
    public async Task Handle_NoSnapshotYet_HasNoIncome()
    {
        Arrange(null);

        var result = await CreateHandler().Handle(new GetMonthlyIncomeLensQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(MonthlyIncomeLensStatus.NoNetWorth);
        result.NetWorth.Should().Be(0m);
        result.MonthlyIncome.Should().BeNull();
        result.HasStaleSleeves.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NegativeNetWorth_HasNoIncome()
    {
        Arrange(-12_000m);

        var result = await CreateHandler().Handle(new GetMonthlyIncomeLensQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(MonthlyIncomeLensStatus.NoNetWorth);
        result.NetWorth.Should().Be(-12_000m);
        result.MonthlyIncome.Should().BeNull();
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
