namespace FinanceSentry.Tests.Unit.Wealth;

using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>#454 S1: burn, savings rate and runway. Every figure here is synthetic.</summary>
public class GetCashflowProfileQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<INetWorthSnapshotRepository> _snapshots = new();
    private readonly Mock<IHonestMonthlyFlowReader> _monthlyFlow = new();
    private readonly Mock<IUserFireAssumptionsReader> _assumptions = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private GetCashflowProfileQueryHandler CreateHandler() =>
        new(new ProjectionInputsReader(_snapshots.Object, _monthlyFlow.Object, _assumptions.Object, _clock));

    private static NetWorthSnapshot Snapshot(decimal? cashTotal, string? staleSleeves = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        SnapshotDate = new DateOnly(2026, 9, 20),
        TotalNetWorth = 90_000m,
        TakenAt = Now,
        CashTotal = cashTotal,
        StaleSleeves = staleSleeves,
    };

    private void Given(NetWorthSnapshot? snapshot, params HonestMonthlyFlow[] flows)
    {
        _snapshots.Setup(r => r.GetLatestByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(flows);
    }

    private Task<CashflowProfileResponse> Read() =>
        CreateHandler().Handle(new GetCashflowProfileQuery(UserId), CancellationToken.None);

    // Four complete months of income 4,000 and the in-progress month, which must never count.
    private static HonestMonthlyFlow[] SteadyMonths() =>
    [
        new("2026-05", 3_000m, 1_000m, 600m),
        new("2026-06", 3_200m, 800m, 640m),
        new("2026-07", 2_800m, 1_200m, 560m),
        new("2026-08", 3_000m, 1_000m, 600m),
        new("2026-09", 100m, -100m, 0m),
    ];

    [Fact]
    public async Task Handle_ComputesMedianBurnAndIncome_PooledRate_AndRunway_OverCompleteMonthsOnly()
    {
        Given(Snapshot(cashTotal: 18_000m), SteadyMonths());

        var result = await Read();

        result.Status.Should().Be(CashflowProfileStatus.Shown);
        result.CompleteMonths.Should().Be(4);
        result.FromMonth.Should().Be("2026-05");
        result.ToMonth.Should().Be("2026-08");
        result.BurnMonthlyMedian.Should().Be(3_000m);
        result.IncomeMonthlyMedian.Should().Be(4_000m);
        result.SavingsRate.Should().Be(0.25m);
        result.RunwayMonths.Should().Be(6.0m);
        result.RunwayReason.Should().BeNull();
        result.Reason.Should().BeNull();
        result.CommittedShare.Should().Be(0.2m);
        result.HasStaleSleeves.Should().BeFalse();
        result.BaseCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_SavingsRate_IsPooled_NotTheMeanOfMonthlyRatios()
    {
        // A thin month (income 100, net -900) would read -900% on its own; pooled it is a small drag on the window.
        Given(
            Snapshot(cashTotal: 10_000m),
            new HonestMonthlyFlow("2026-06", 1_000m, -900m),
            new HonestMonthlyFlow("2026-07", 2_000m, 2_000m),
            new HonestMonthlyFlow("2026-08", 2_000m, 2_000m));

        var result = await Read();

        // (-900 + 2000 + 2000) / (100 + 4000 + 4000) = 3100 / 8100
        result.SavingsRate.Should().Be(0.3827m);
    }

    [Fact]
    public async Task Handle_Dissaving_ReturnsANegativeRate()
    {
        Given(
            Snapshot(cashTotal: 10_000m),
            new HonestMonthlyFlow("2026-06", 5_000m, -1_000m),
            new HonestMonthlyFlow("2026-07", 5_000m, -1_000m),
            new HonestMonthlyFlow("2026-08", 5_000m, -1_000m));

        var result = await Read();

        result.Status.Should().Be(CashflowProfileStatus.Shown);
        result.SavingsRate.Should().Be(-0.25m);
        result.BurnMonthlyMedian.Should().Be(5_000m);
        result.RunwayMonths.Should().Be(2.0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Handle_UnderThreeCompleteMonths_IsInsufficientHistory_AndShowsNoFigure(int complete)
    {
        var flows = SteadyMonths().Take(complete).Append(new HonestMonthlyFlow("2026-09", 100m, -100m)).ToArray();
        Given(Snapshot(cashTotal: 18_000m), flows);

        var result = await Read();

        result.Status.Should().Be(CashflowProfileStatus.InsufficientHistory);
        result.Reason.Should().Be("INSUFFICIENT_MONTHS");
        result.CompleteMonths.Should().Be(complete);
        result.SavingsRate.Should().BeNull();
        result.BurnMonthlyMedian.Should().BeNull();
        result.IncomeMonthlyMedian.Should().BeNull();
        result.RunwayMonths.Should().BeNull();
        result.CommittedShare.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PooledIncomeNotPositive_WithholdsTheRate()
    {
        Given(
            Snapshot(cashTotal: 10_000m),
            new HonestMonthlyFlow("2026-06", 1_000m, -1_000m),
            new HonestMonthlyFlow("2026-07", 1_000m, -1_000m),
            new HonestMonthlyFlow("2026-08", 1_000m, -1_000m));

        var result = await Read();

        result.Status.Should().Be(CashflowProfileStatus.InsufficientHistory);
        result.Reason.Should().Be("NO_POSITIVE_INCOME");
        result.SavingsRate.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SnapshotWithoutCashSplit_ShowsTheRest_AndNamesWhyRunwayIsMissing()
    {
        Given(Snapshot(cashTotal: null), SteadyMonths());

        var result = await Read();

        result.Status.Should().Be(CashflowProfileStatus.Shown);
        result.RunwayMonths.Should().BeNull();
        result.RunwayReason.Should().Be("NO_CASH_SPLIT");
        result.BurnMonthlyMedian.Should().Be(3_000m);
        result.SavingsRate.Should().Be(0.25m);
    }

    [Fact]
    public async Task Handle_NoSnapshot_ShowsTheRest_AndNamesWhyRunwayIsMissing()
    {
        Given(null, SteadyMonths());

        var result = await Read();

        result.Status.Should().Be(CashflowProfileStatus.Shown);
        result.RunwayMonths.Should().BeNull();
        result.RunwayReason.Should().Be("NO_SNAPSHOT");
        result.SavingsRate.Should().Be(0.25m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2_500)]
    public async Task Handle_ZeroOrNegativeCash_IsNoCash_WithBurnAndRateStillShown(int cash)
    {
        Given(Snapshot(cashTotal: cash), SteadyMonths());

        var result = await Read();

        result.Status.Should().Be(CashflowProfileStatus.NoCash);
        result.RunwayMonths.Should().BeNull();
        result.RunwayReason.Should().Be("NO_CASH");
        result.BurnMonthlyMedian.Should().Be(3_000m);
        result.SavingsRate.Should().Be(0.25m);
    }

    [Fact]
    public async Task Handle_NoSpending_HasNoRunway_NotAnInfiniteOne()
    {
        Given(
            Snapshot(cashTotal: 10_000m),
            new HonestMonthlyFlow("2026-06", 0m, 4_000m),
            new HonestMonthlyFlow("2026-07", 0m, 4_000m),
            new HonestMonthlyFlow("2026-08", 0m, 4_000m));

        var result = await Read();

        result.Status.Should().Be(CashflowProfileStatus.Shown);
        result.SavingsRate.Should().Be(1m);
        result.RunwayMonths.Should().BeNull();
        result.RunwayReason.Should().Be("NO_BURN");
        result.CommittedShare.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CommittedShare_IsNullWhenTheReaderDidNotSupplyIt()
    {
        Given(
            Snapshot(cashTotal: 10_000m),
            new HonestMonthlyFlow("2026-06", 3_000m, 1_000m),
            new HonestMonthlyFlow("2026-07", 3_000m, 1_000m),
            new HonestMonthlyFlow("2026-08", 3_000m, 1_000m));

        var result = await Read();

        result.CommittedShare.Should().BeNull();
        result.BurnMonthlyMedian.Should().Be(3_000m);
    }

    [Fact]
    public async Task Handle_FlagsStaleSleeves()
    {
        Given(Snapshot(cashTotal: 18_000m, staleSleeves: "crypto"), SteadyMonths());

        var result = await Read();

        result.HasStaleSleeves.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OutOfOrderMonths_StillNamesTheOldestAndNewest()
    {
        Given(
            Snapshot(cashTotal: 18_000m),
            new HonestMonthlyFlow("2026-08", 3_000m, 1_000m),
            new HonestMonthlyFlow("2026-06", 3_000m, 1_000m),
            new HonestMonthlyFlow("2026-07", 3_000m, 1_000m));

        var result = await Read();

        result.FromMonth.Should().Be("2026-06");
        result.ToMonth.Should().Be("2026-08");
    }

    [Fact]
    public async Task Response_SaysTheFiguresAreContext_AndSerializesTheStatusByName()
    {
        Given(Snapshot(cashTotal: 18_000m), SteadyMonths());

        var result = await Read();
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        result.Disclosure.Should().Contain("never a trading signal");
        json.Should().Contain("\"status\":\"Shown\"");
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
