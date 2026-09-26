namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetFireProjectionQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<INetWorthSnapshotRepository> _snapshots = new();
    private readonly Mock<IHonestMonthlyFlowReader> _monthlyFlow = new();
    private readonly Mock<IUserFireAssumptionsReader> _assumptions = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private GetFireProjectionQueryHandler CreateHandler() =>
        new(_snapshots.Object, _monthlyFlow.Object, _assumptions.Object, _clock);

    private static NetWorthSnapshot Snapshot(decimal totalNetWorth, string? staleSleeves = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        SnapshotDate = new DateOnly(2026, 9, 20),
        TotalNetWorth = totalNetWorth,
        TakenAt = Now,
        StaleSleeves = staleSleeves,
    };

    private static IReadOnlyList<HonestMonthlyFlow> ThreeCompleteMonths(decimal outflowUsd, decimal netUsd) =>
    [
        new("2026-06", outflowUsd, netUsd),
        new("2026-07", outflowUsd, netUsd),
        new("2026-08", outflowUsd, netUsd),
        new("2026-09", outflowUsd, netUsd), // current month, excluded by the handler
    ];

    [Fact]
    public async Task Handle_UsesLatestSnapshot_AndFlagsStaleSleeves()
    {
        _snapshots.Setup(r => r.GetLatestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(100_000m, staleSleeves: "crypto"));
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreeCompleteMonths(outflowUsd: 3_000m, netUsd: 1_000m));
        _assumptions.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FireAssumptions(0.04m, 0.05m));

        var result = await CreateHandler().Handle(new GetFireProjectionQuery(UserId), CancellationToken.None);

        result.CurrentNetWorth.Should().Be(100_000m);
        result.HasStaleSleeves.Should().BeTrue();
        result.Status.Should().Be(FireProjectionStatus.Projected);
    }

    [Fact]
    public async Task Handle_NoStaleSleeves_ReportsFalse()
    {
        _snapshots.Setup(r => r.GetLatestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(100_000m, staleSleeves: null));
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreeCompleteMonths(outflowUsd: 3_000m, netUsd: 1_000m));
        _assumptions.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FireAssumptions(0.04m, 0.05m));

        var result = await CreateHandler().Handle(new GetFireProjectionQuery(UserId), CancellationToken.None);

        result.HasStaleSleeves.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ExcludesCurrentInProgressMonth_FromTheMedianWindow()
    {
        _snapshots.Setup(r => r.GetLatestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(100_000m));
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<HonestMonthlyFlow>)
            [
                new("2026-06", 3_000m, 1_000m),
                new("2026-07", 3_000m, 1_000m),
                new("2026-09", 999_999m, 999_999m), // current month — must be dropped, or this would swamp the median
            ]);
        _assumptions.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FireAssumptions(0.04m, 0.05m));

        var result = await CreateHandler().Handle(new GetFireProjectionQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(FireProjectionStatus.InsufficientHistory);
    }

    [Fact]
    public async Task Handle_FewerThanThreeCompleteMonths_ReturnsInsufficientHistory()
    {
        _snapshots.Setup(r => r.GetLatestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(100_000m));
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<HonestMonthlyFlow>)
            [
                new("2026-08", 3_000m, 1_000m),
                new("2026-09", 3_000m, 1_000m),
            ]);
        _assumptions.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FireAssumptions(0.04m, 0.05m));

        var result = await CreateHandler().Handle(new GetFireProjectionQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(FireProjectionStatus.InsufficientHistory);
        result.ProjectedDate.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoSnapshotYet_ReturnsInsufficientHistoryWithZeroNetWorth()
    {
        _snapshots.Setup(r => r.GetLatestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NetWorthSnapshot?)null);
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreeCompleteMonths(outflowUsd: 3_000m, netUsd: 1_000m));
        _assumptions.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FireAssumptions(0.04m, 0.05m));

        var result = await CreateHandler().Handle(new GetFireProjectionQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(FireProjectionStatus.InsufficientHistory);
        result.CurrentNetWorth.Should().Be(0m);
        result.HasStaleSleeves.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MissingUserAssumptions_FallsBackToDefaultRates()
    {
        _snapshots.Setup(r => r.GetLatestByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(100_000m));
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreeCompleteMonths(outflowUsd: 3_000m, netUsd: 1_000m));
        _assumptions.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FireAssumptions?)null);

        var result = await CreateHandler().Handle(new GetFireProjectionQuery(UserId), CancellationToken.None);

        result.SafeWithdrawalRate.Should().Be(0.04m);
        result.RealAnnualReturn.Should().Be(0.05m);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
