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

public class GetNetWorthProjectionQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<INetWorthSnapshotRepository> _snapshots = new();
    private readonly Mock<IHonestMonthlyFlowReader> _monthlyFlow = new();
    private readonly Mock<IUserFireAssumptionsReader> _assumptions = new();
    private readonly FakeCareerEventRepository _careerEvents = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private GetNetWorthProjectionQueryHandler CreateHandler() =>
        new(
            new ProjectionInputsReader(_snapshots.Object, _monthlyFlow.Object, _assumptions.Object, _clock),
            new ScenarioReader(_careerEvents, _clock));

    private void Arrange(decimal? netWorth, IReadOnlyList<HonestMonthlyFlow> flows, FireAssumptions? assumptions = null)
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
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, Projection.LookbackMonths, It.IsAny<CancellationToken>()))
            .ReturnsAsync(flows);
        _assumptions.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(assumptions);
    }

    private static IReadOnlyList<HonestMonthlyFlow> Months(params decimal[] net) =>
    [
        .. net.Select((n, i) => new HonestMonthlyFlow($"2026-0{3 + i}", 3_000m, n)),
        new("2026-09", 0m, 999_999m), // current month, never part of the savings path
    ];

    [Fact]
    public async Task Handle_ProjectsTwelveMonths_AsARangeAroundTheCentralReturn()
    {
        Arrange(100_000m, Months(1_000m, 50_000m, 1_200m, 900m));

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(NetWorthProjectionStatus.Projected);
        result.HorizonMonths.Should().Be(12);
        result.CurrentNetWorth.Should().Be(100_000m);
        result.MonthlySavings.Should().Be(1_100m, "the median of the four complete months, not the outlier");
        result.CompleteMonths.Should().Be(4);
        result.RealAnnualReturnRange.Should().Be(new ProjectionRange(0.02m, 0.05m, 0.08m));
        result.ProjectedNetWorth.Should().Be(
            Projection.ValueAfter(12, 100_000m, 1_100m, ProjectionAssumptions.Default));
        var range = result.ProjectedNetWorth!.Value;
        range.Low.Should().BeLessThan(range.Central);
        range.Central.Should().BeLessThan(range.High);
    }

    [Fact]
    public async Task Handle_UsesThePersonsReturnAssumption_TheSameOneTheFireProjectionUses()
    {
        Arrange(100_000m, Months(1_000m, 1_000m, 1_000m), new FireAssumptions(0.035m, 0.07m));

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        result.RealAnnualReturnRange.Should().Be(new ProjectionRange(0.04m, 0.07m, 0.10m));
    }

    [Fact]
    public async Task Handle_FewerThanThreeCompleteMonths_IsInsufficientHistoryWithNoFigure()
    {
        Arrange(100_000m, Months(1_000m, 1_000m));

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(NetWorthProjectionStatus.InsufficientHistory);
        result.ProjectedNetWorth.Should().BeNull();
        result.CompleteMonths.Should().Be(2);
        result.CurrentNetWorth.Should().Be(100_000m);
    }

    [Fact]
    public async Task Handle_NoSnapshotYet_IsInsufficientHistory()
    {
        Arrange(null, Months(1_000m, 1_000m, 1_000m));

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(NetWorthProjectionStatus.InsufficientHistory);
        result.CurrentNetWorth.Should().Be(0m);
        result.HasStaleSleeves.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NegativeSavings_StillProjects_TheRangeShowsTheDrawdown()
    {
        Arrange(10_000m, Months(-500m, -500m, -500m), new FireAssumptions(0.04m, 0m));

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(NetWorthProjectionStatus.Projected);
        result.ProjectedNetWorth!.Value.Central.Should().Be(4_000m);
    }

    private void AddEvent(CareerEventKind kind, DateOnly date, decimal amount, DateOnly? end = null, bool retired = false)
    {
        var career = CareerEvent.Create(UserId, kind, date, end, "Event", amount, "USD", null);
        if (retired)
            career.Retire(Now);
        _careerEvents.Events.Add(career);
    }

    [Fact]
    public async Task Handle_NoCareerEvents_HasNoScenario_AndTheBaselineIsUnchanged()
    {
        Arrange(100_000m, Months(1_000m, 1_000m, 1_000m));

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        result.Scenario.Should().BeNull();
        result.ProjectedNetWorth.Should().Be(Projection.ValueAfter(12, 100_000m, 1_000m, ProjectionAssumptions.Default));
    }

    [Fact]
    public async Task Handle_CareerEvents_AddALabelledScenarioBesideTheBaseline_NeverReplacingIt()
    {
        Arrange(100_000m, Months(1_000m, 1_000m, 1_000m));
        AddEvent(CareerEventKind.IncomeStep, new DateOnly(2026, 12, 1), 500m);
        AddEvent(CareerEventKind.OneOffOut, new DateOnly(2027, 2, 10), 900m);

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        var baseline = Projection.ValueAfter(12, 100_000m, 1_000m, ProjectionAssumptions.Default);
        result.ProjectedNetWorth.Should().Be(baseline, "the baseline stays the default");
        result.Scenario.Should().NotBeNull();
        result.Scenario!.Status.Should().Be(ScenarioStatus.Applied);
        result.Scenario.EventCount.Should().Be(2);
        result.Scenario.Label.Should().Be("A scenario you entered, not a forecast.");
        var deltas = ScenarioPath.MonthlyDeltas(
            [
                new ScenarioEvent(ScenarioEventKind.IncomeStep, new DateOnly(2026, 12, 1), null, 500m),
                new ScenarioEvent(ScenarioEventKind.OneOffOut, new DateOnly(2027, 2, 10), null, 900m),
            ],
            new DateOnly(2026, 9, 26),
            12);
        result.Scenario.ProjectedNetWorth.Should().Be(Projection.ValueAfter(12, 100_000m, 1_000m, ProjectionAssumptions.Default, deltas));
        result.Scenario.ProjectedNetWorth!.Value.Central.Should().BeGreaterThan(baseline.Central);
    }

    [Fact]
    public async Task Handle_RetiredPastAndNotYetStartedEvents_NeverCount()
    {
        Arrange(100_000m, Months(1_000m, 1_000m, 1_000m));
        AddEvent(CareerEventKind.IncomeStep, new DateOnly(2026, 12, 1), 500m, retired: true);
        AddEvent(CareerEventKind.IncomeStep, new DateOnly(2026, 6, 1), 700m);

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        result.Scenario.Should().BeNull("a retired event and one already in the baseline's history are not a scenario");
    }

    [Fact]
    public async Task Handle_OnAHostWithoutTheKey_SaysTheScenarioIsUnreadable_AndClaimsNoFigure()
    {
        Arrange(100_000m, Months(1_000m, 1_000m, 1_000m));
        _careerEvents.CanReadEncryptedFields = false;
        AddEvent(CareerEventKind.IncomeStep, new DateOnly(2026, 12, 1), 500m);
        AddEvent(CareerEventKind.OneOffOut, new DateOnly(2027, 2, 10), 900m);

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        result.Scenario!.Status.Should().Be(ScenarioStatus.Unreadable);
        result.Scenario.EventCount.Should().Be(2, "the events count even though they cannot be read");
        result.Scenario.ProjectedNetWorth.Should().BeNull();
        result.ProjectedNetWorth.Should().NotBeNull("the baseline needs no key");
    }

    [Fact]
    public async Task Handle_WithoutEnoughHistory_HasNoScenarioEither()
    {
        Arrange(100_000m, Months(1_000m, 1_000m));
        AddEvent(CareerEventKind.IncomeStep, new DateOnly(2026, 12, 1), 500m);

        var result = await CreateHandler().Handle(new GetNetWorthProjectionQuery(UserId), CancellationToken.None);

        result.Status.Should().Be(NetWorthProjectionStatus.InsufficientHistory);
        result.Scenario.Should().BeNull();
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
