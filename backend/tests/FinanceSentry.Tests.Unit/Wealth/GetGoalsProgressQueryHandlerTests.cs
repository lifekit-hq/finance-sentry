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

public class GetGoalsProgressQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 26);

    private readonly Mock<INetWorthSnapshotRepository> _snapshots = new();
    private readonly Mock<IHonestMonthlyFlowReader> _monthlyFlow = new();
    private readonly Mock<IUserFireAssumptionsReader> _assumptions = new();
    private readonly Mock<IGoalsReader> _goals = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private GetGoalsProgressQueryHandler CreateHandler() => new(
        new ProjectionInputsReader(_snapshots.Object, _monthlyFlow.Object, _assumptions.Object, _clock),
        _goals.Object,
        _clock);

    private void Arrange(
        decimal? netWorth,
        IReadOnlyList<HonestMonthlyFlow> flows,
        IpsGoals? ips,
        FireAssumptions? assumptions = null)
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
        _goals.Setup(r => r.GetCurrentAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(ips);
    }

    private static IReadOnlyList<HonestMonthlyFlow> Months(params decimal[] net) =>
    [
        .. net.Select((n, i) => new HonestMonthlyFlow($"2026-0{3 + i}", 3_000m, n)),
        new("2026-09", 0m, 999_999m), // current month, never part of the savings path
    ];

    private static IpsGoals Goals(GoalContributionPlan? plan = null, params GoalTarget[] goals) => new(goals, plan);

    private static GoalTarget Goal(decimal? amount, DateOnly? date, string label = "House deposit") =>
        new(label, amount, date, "high");

    private Task<GoalsProgressResponse> Run() =>
        CreateHandler().Handle(new GetGoalsProgressQuery(UserId), CancellationToken.None);

    // --- no goals --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_NoCurrentIps_IsNoGoals()
    {
        Arrange(100_000m, Months(1_000m, 1_000m, 1_000m), ips: null);

        var result = await Run();

        result.Status.Should().Be(GoalsProgressStatus.NoGoals);
        result.Goals.Should().BeEmpty();
        result.ContributionPlan.Should().BeNull();
        result.CurrentNetWorth.Should().Be(100_000m);
    }

    [Fact]
    public async Task Handle_IpsWithoutGoals_IsNoGoals_EvenWithAContributionPlan()
    {
        Arrange(100_000m, Months(1_000m, 1_000m, 1_000m), Goals(new GoalContributionPlan(500m, "monthly")));

        var result = await Run();

        result.Status.Should().Be(GoalsProgressStatus.NoGoals);
        result.ContributionPlan.Should().BeNull("there is nothing to compare the plan with when no goal is set");
    }

    // --- on track / behind -----------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_SavingsPathReachesTheTarget_IsOnTrack_WithTheRequiredSavingAsARange()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(130_000m, new DateOnly(2028, 9, 26))));

        var result = await Run();

        var goal = result.Goals.Single();
        result.Status.Should().Be(GoalsProgressStatus.Shown);
        goal.OnTrack.Should().Be(GoalTrack.OnTrack);
        goal.MonthsLeft.Should().Be(24);
        goal.TargetAmount.Should().Be(130_000m);
        goal.AlreadyReached.Should().BeFalse();
        goal.ProjectedAtTargetDate!.Value.Central.Should().BeGreaterThanOrEqualTo(130_000m);

        // The required saving, fed back through the shared projection, lands on the target at every return.
        var required = goal.RequiredMonthlySaving!.Value;
        required.Low.Should().BeLessThan(required.Central, "a higher return needs less saving");
        required.Central.Should().BeLessThan(required.High);
        Projection.ValueAfter(24, 100_000m, required.Central, ProjectionAssumptions.Default).Central
            .Should().BeApproximately(130_000m, 0.5m);
        required.Central.Should().BeLessThan(1_000m, "the savings path saves more than the goal needs");
    }

    [Fact]
    public async Task Handle_SavingsPathFallsShort_IsBehind()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(200_000m, new DateOnly(2028, 9, 26))));

        var result = await Run();

        var goal = result.Goals.Single();
        goal.OnTrack.Should().Be(GoalTrack.Behind);
        goal.RequiredMonthlySaving!.Value.Central.Should().BeGreaterThan(1_000m);
        goal.ProjectedAtTargetDate!.Value.Central.Should().BeLessThan(200_000m);
    }

    [Fact]
    public async Task Handle_UsesThePersonsReturnAssumption_NotASecondSet()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(130_000m, new DateOnly(2028, 9, 26))),
            new FireAssumptions(SafeWithdrawalRate: 0.04m, RealAnnualReturn: 0m));

        var result = await Run();

        result.RealAnnualReturnRange.Should().Be(new ProjectionRange(0m, 0m, 0.03m));
        // At a zero return the required saving is the plain gap over the months: 30,000 / 24.
        result.Goals.Single().RequiredMonthlySaving!.Value.Low.Should().BeLessThan(1_250m);
        result.Goals.Single().RequiredMonthlySaving!.Value.High.Should().Be(1_250m);
    }

    [Fact]
    public async Task Handle_NegativeSavingsPath_IsBehind_AndTheProjectionShowsTheDrawdown()
    {
        Arrange(
            100_000m, Months(-500m, -500m, -500m),
            Goals(plan: null, Goal(101_000m, new DateOnly(2027, 9, 26))),
            new FireAssumptions(0.04m, 0m));

        var result = await Run();

        var goal = result.Goals.Single();
        goal.OnTrack.Should().Be(GoalTrack.Behind);
        goal.ProjectedAtTargetDate!.Value.Low.Should().BeLessThan(100_000m);
    }

    // --- already reached -------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_NetWorthAlreadyAtTheTarget_NeedsNoSaving()
    {
        Arrange(
            150_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(130_000m, new DateOnly(2028, 9, 26))));

        var goal = (await Run()).Goals.Single();

        goal.AlreadyReached.Should().BeTrue();
        goal.OnTrack.Should().Be(GoalTrack.OnTrack);
        goal.RequiredMonthlySaving.Should().Be(new ProjectionRange(0m, 0m, 0m));
    }

    // --- months left ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2027, 9, 25, 11)]
    [InlineData(2027, 9, 26, 12)]
    [InlineData(2026, 10, 27, 1)]
    [InlineData(2026, 10, 25, 1)]
    [InlineData(2026, 9, 27, 1)]
    [InlineData(2026, 9, 26, 0)]
    public async Task Handle_MonthsLeft_CountsWholeMonthsToTheDate(int year, int month, int day, int expected)
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(200_000m, new DateOnly(year, month, day))));

        var goal = (await Run()).Goals.Single();

        goal.MonthsLeft.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_TargetDatePassedShortOfTheTarget_IsBehind_WithNothingToProject()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(200_000m, Today.AddMonths(-2))));

        var goal = (await Run()).Goals.Single();

        goal.OnTrack.Should().Be(GoalTrack.Behind);
        goal.Reason.Should().Be(GoalsProgressReasons.TargetDatePassed);
        goal.MonthsLeft.Should().Be(0);
        goal.RequiredMonthlySaving.Should().BeNull();
        goal.ProjectedAtTargetDate.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TargetDateBeyondTheGoalHorizon_IsNoTargetDate_AndLeavesOtherGoalsTracked()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(
                plan: null,
                Goal(200_000m, new DateOnly(9999, 12, 31), "Someday"),
                Goal(130_000m, new DateOnly(2028, 9, 26))));

        var goals = (await Run()).Goals;

        var far = goals.Single(g => g.Label == "Someday");
        far.OnTrack.Should().Be(GoalTrack.NoTargetDate);
        far.Reason.Should().Be(GoalsProgressReasons.TargetDateTooFar);
        far.MonthsLeft.Should().BeGreaterThan(1200);
        far.RequiredMonthlySaving.Should().BeNull();
        far.ProjectedAtTargetDate.Should().BeNull();

        var near = goals.Single(g => g.Label == "House deposit");
        near.Reason.Should().BeNull();
        near.RequiredMonthlySaving.Should().NotBeNull();
        near.ProjectedAtTargetDate.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_TargetDatePassedWithTheTargetReached_IsOnTrack()
    {
        Arrange(
            250_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(200_000m, Today.AddMonths(-2))));

        var goal = (await Run()).Goals.Single();

        goal.OnTrack.Should().Be(GoalTrack.OnTrack);
        goal.AlreadyReached.Should().BeTrue();
    }

    // --- goals that cannot be tracked ------------------------------------------------------------------------

    [Fact]
    public async Task Handle_GoalWithoutADate_IsNoTargetDate_AndClaimsNothingElse()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(200_000m, date: null)));

        var goal = (await Run()).Goals.Single();

        goal.OnTrack.Should().Be(GoalTrack.NoTargetDate);
        goal.MonthsLeft.Should().BeNull();
        goal.RequiredMonthlySaving.Should().BeNull();
        goal.ProjectedAtTargetDate.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Handle_GoalWithoutAPositiveAmount_IsNoTargetAmount(int? amount)
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(amount, new DateOnly(2028, 9, 26))));

        var goal = (await Run()).Goals.Single();

        goal.OnTrack.Should().Be(GoalTrack.NoTargetAmount);
        goal.RequiredMonthlySaving.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoSnapshot_IsInsufficientHistory_WithNoNetWorth()
    {
        Arrange(
            netWorth: null, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(200_000m, new DateOnly(2028, 9, 26))));

        var result = await Run();

        result.CurrentNetWorth.Should().BeNull();
        var goal = result.Goals.Single();
        goal.OnTrack.Should().Be(GoalTrack.InsufficientHistory);
        goal.Reason.Should().Be(GoalsProgressReasons.NoSnapshot);
        goal.RequiredMonthlySaving.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UnderThreeCompleteMonths_IsInsufficientHistory_ButStillStatesTheRequiredSaving()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m),
            Goals(plan: null, Goal(200_000m, new DateOnly(2028, 9, 26))));

        var result = await Run();

        result.MonthlySavings.Should().BeNull();
        result.CompleteMonths.Should().Be(2);
        var goal = result.Goals.Single();
        goal.OnTrack.Should().Be(GoalTrack.InsufficientHistory);
        goal.Reason.Should().Be(GoalsProgressReasons.InsufficientMonths);
        goal.RequiredMonthlySaving.Should().NotBeNull("the gap to the target needs no savings history");
        goal.ProjectedAtTargetDate.Should().BeNull();
    }

    [Fact]
    public async Task Handle_KeepsGoalsInTheOrderTheIpsHoldsThem()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(
                plan: null,
                Goal(300_000m, new DateOnly(2030, 1, 1), "Retire"),
                Goal(50_000m, new DateOnly(2027, 6, 1), "Car")));

        var result = await Run();

        result.Goals.Select(g => g.Label).Should().Equal("Retire", "Car");
    }

    // --- the contribution plan against the measured path -----------------------------------------------------

    [Fact]
    public async Task Handle_MonthlyPlan_IsComparedWithTheMeasuredSavingsPath()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(new GoalContributionPlan(1_500m, "monthly"), Goal(200_000m, new DateOnly(2028, 9, 26))));

        var plan = (await Run()).ContributionPlan!;

        plan.PlannedAmount.Should().Be(1_500m);
        plan.PlannedMonthly.Should().Be(1_500m);
        plan.ActualMonthlySavings.Should().Be(1_000m);
        plan.Difference.Should().Be(-500m, "saving less than planned reads negative");
        plan.Reason.Should().BeNull();
    }

    [Theory]
    [InlineData("weekly", 100, 433.33)]
    [InlineData("Biweekly", 100, 216.67)]
    [InlineData(" quarterly ", 900, 300)]
    [InlineData("annual", 1200, 100)]
    public async Task Handle_PlanCadence_IsConvertedToItsMonthlyEquivalent(string cadence, int amount, double monthly)
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(new GoalContributionPlan(amount, cadence), Goal(200_000m, new DateOnly(2028, 9, 26))));

        var plan = (await Run()).ContributionPlan!;

        plan.PlannedMonthly.Should().Be((decimal)monthly);
        plan.Difference.Should().Be(1_000m - (decimal)monthly);
    }

    [Fact]
    public async Task Handle_UnrecognizedCadence_IsReportedAsIs_WithNoMonthlyEquivalent()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(new GoalContributionPlan(100m, "whenever"), Goal(200_000m, new DateOnly(2028, 9, 26))));

        var plan = (await Run()).ContributionPlan!;

        plan.Cadence.Should().Be("whenever");
        plan.PlannedMonthly.Should().BeNull();
        plan.Difference.Should().BeNull();
        plan.Reason.Should().Be(GoalsProgressReasons.UnrecognizedCadence);
    }

    [Fact]
    public async Task Handle_PlanWithoutASavingsPath_HasNoActualAndNoDifference()
    {
        Arrange(
            100_000m, Months(1_000m),
            Goals(new GoalContributionPlan(500m, "monthly"), Goal(200_000m, new DateOnly(2028, 9, 26))));

        var plan = (await Run()).ContributionPlan!;

        plan.PlannedMonthly.Should().Be(500m);
        plan.ActualMonthlySavings.Should().BeNull();
        plan.Difference.Should().BeNull();
        plan.Reason.Should().Be(GoalsProgressReasons.InsufficientMonths);
    }

    [Fact]
    public async Task Handle_NoPlan_HasNoComparison()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(200_000m, new DateOnly(2028, 9, 26))));

        (await Run()).ContributionPlan.Should().BeNull();
    }

    // --- wording ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_StatesWhatGoalsAreMeasuredAgainst_AndCarriesTheProjectionDisclosure()
    {
        Arrange(
            100_000m, Months(1_000m, 1_000m, 1_000m),
            Goals(plan: null, Goal(200_000m, new DateOnly(2028, 9, 26))));

        var result = await Run();

        result.Basis.Should().Contain("whole net worth plus your savings");
        result.Disclosure.Should().StartWith("Hypothetical, not a guarantee.");
        result.Disclosure.Should().Contain("never a trading signal");
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
