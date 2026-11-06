namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>#454 S7: the financial-profile aggregate. Every figure is synthetic USD; sections compose the single-purpose readers.</summary>
public sealed class GetFinancialProfileQueryHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>> _cashflow = new();
    private readonly Mock<IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse>> _goals = new();
    private readonly Mock<IQueryHandler<GetIncomeProfileQuery, IncomeProfileResponse>> _income = new();
    private readonly Mock<IQueryHandler<GetCareerTimelineQuery, CareerTimelineResponse>> _career = new();
    private readonly Mock<INetWorthSnapshotRepository> _snapshots = new();
    private readonly Mock<IHonestMonthlyFlowReader> _monthlyFlow = new();
    private readonly Mock<IUserFireAssumptionsReader> _assumptions = new();
    private readonly Mock<IDisposalTaxSummaryReader> _tax = new();
    private readonly Mock<IRiskProfileReader> _risk = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public GetFinancialProfileQueryHandlerTests()
    {
        Cashflow(Shown());
        Goals(OneDatedGoal());
        Income(IncomeWith(IncomeVariance.Matches));
        Career(new CareerTimelineResponse(true, [Event()], 1));
        Tax(new DisposalTaxSummary(2, 2, 0, 410m, 2026, new Dictionary<string, int>()));
        Risk(new IpsRiskProfile(3, 3, 25m, Now.AddDays(-30), false, 20, 15_000m));
        Months(5_000m, 5_200m, 5_100m, 4_900m, 5_000m, 5_100m);
    }

    private GetFinancialProfileQueryHandler CreateHandler() => new(
        _cashflow.Object, _goals.Object, _income.Object, _career.Object,
        new ProjectionInputsReader(_snapshots.Object, _monthlyFlow.Object, _assumptions.Object, _clock),
        _tax.Object, _risk.Object, _clock, NullLogger<GetFinancialProfileQueryHandler>.Instance);

    private Task<FinancialProfileResponse> Read() =>
        CreateHandler().Handle(new GetFinancialProfileQuery(UserId), CancellationToken.None);

    private void Cashflow(CashflowProfileResponse r) =>
        _cashflow.Setup(h => h.Handle(It.IsAny<GetCashflowProfileQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(r);

    private void Goals(GoalsProgressResponse r) =>
        _goals.Setup(h => h.Handle(It.IsAny<GetGoalsProgressQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(r);

    private void Income(IncomeProfileResponse r) =>
        _income.Setup(h => h.Handle(It.IsAny<GetIncomeProfileQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(r);

    private void Career(CareerTimelineResponse r) =>
        _career.Setup(h => h.Handle(It.IsAny<GetCareerTimelineQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(r);

    private void Tax(DisposalTaxSummary r) =>
        _tax.Setup(h => h.ReadAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(r);

    private void Risk(IpsRiskProfile? r) =>
        _risk.Setup(h => h.GetCurrentAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(r);

    private void Months(params decimal[] incomes) => _monthlyFlow
        .Setup(f => f.GetMonthlyFlowAsync(UserId, Projection.LookbackMonths, It.IsAny<CancellationToken>()))
        .ReturnsAsync([.. incomes.Select((income, i) => new HonestMonthlyFlow($"2026-0{3 + i}", 3_000m, income - 3_000m))]);

    private static CashflowProfileResponse Shown(decimal? runway = 30m, string? runwayReason = null, bool stale = false) => new(
        CashflowProfileStatus.Shown, 6, "2026-03", "2026-08", 5_000m, 2_000m, 0.4m, runway, runwayReason, null, stale);

    private static CashflowProfileResponse NoHistory() => new(
        CashflowProfileStatus.InsufficientHistory, 1, null, null, null, null, null, null, null, null, false,
        CashflowProfileReasons.InsufficientMonths);

    private static GoalsProgressResponse OneDatedGoal(int monthsLeft = 120, GoalTrack track = GoalTrack.OnTrack) => new(
        GoalsProgressStatus.Shown, 100_000m, 1_500m, 6,
        [new GoalProgress("House deposit", 200_000m, new DateOnly(2036, 11, 6), "high", monthsLeft, null, null, false, track)],
        new ContributionPlanComparison(1_000m, "monthly", 1_000m, 1_500m, 500m),
        new ProjectionRange(0.02m, 0.05m, 0.08m), false);

    private static IncomeProfileResponse IncomeWith(IncomeVariance variance) => new(
        true, [], 1, 4_000m, "Net", 3_900m, null, variance);

    private static CareerTimelineEntry Event() => new(
        Guid.NewGuid(), CareerEventKind.IncomeStep, new DateOnly(2027, 1, 1), null, "Raise", 500m, "USD", null, true, false);

    // --- the happy path ---------------------------------------------------------------------------------------

    [Fact]
    public async Task EverySectionOnFile_IsComplete_StampedAsOfNow_AndNothingIsMissing()
    {
        var profile = await Read();

        profile.AsOf.Should().Be(Now);
        profile.Cashflow.DataQuality.Should().Be(DataQuality.Complete);
        profile.Goals.DataQuality.Should().Be(DataQuality.Complete);
        profile.Income.DataQuality.Should().Be(DataQuality.Complete);
        profile.Career.DataQuality.Should().Be(DataQuality.Complete);
        profile.DisposalTax.DataQuality.Should().Be(DataQuality.Complete);
        profile.Risk.DataQuality.Should().Be(DataQuality.Complete);
        profile.MissingSections.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSections_CarryTheFiguresOfTheSinglePurposeReaders()
    {
        var profile = await Read();

        profile.Cashflow.Data!.BurnMonthlyMedian.Should().Be(2_000m);
        profile.Cashflow.Data.RunwayMonths.Should().Be(30m);
        profile.Goals.Data!.ShortestGoalMonths.Should().Be(120);
        profile.Goals.Data.PlannedMonthlyContribution.Should().Be(1_000m);
        profile.Goals.Data.ContributionDifference.Should().Be(500m);
        profile.Income.Data!.DeclaredMonthlyUsd.Should().Be(4_000m);
        profile.Career.Data.Should().Be(new CareerSummary(true, 1, 1));
        profile.DisposalTax.Data!.EstimatedTaxEur.Should().Be(410m);
        profile.Risk.Data!.RiskCapacity.Should().Be(3);
    }

    [Fact]
    public async Task TheSummaries_NeverCarryAnIncomeSourceOrCareerEventLabel()
    {
        Income(IncomeWith(IncomeVariance.Matches) with
        {
            Sources = [new IncomeProfileSource(Guid.NewGuid(), IncomeKind.Salary, "Acme payroll", 4_000m, "USD", IncomeCadence.Monthly, IncomeBasis.Net, new DateOnly(2025, 1, 1), null, true, 4_000m)],
        });

        var json = System.Text.Json.JsonSerializer.Serialize(await Read());

        json.Should().NotContain("Acme payroll");
        json.Should().NotContain("Raise");
    }

    // --- missing and partial sections -------------------------------------------------------------------------

    [Fact]
    public async Task ASectionWithNothingOnFile_IsMissing_AndNamed()
    {
        Goals(new GoalsProgressResponse(
            GoalsProgressStatus.NoGoals, 100_000m, 1_500m, 6, [], null, new ProjectionRange(0.02m, 0.05m, 0.08m), false));
        Income(IncomeWith(IncomeVariance.NoDeclared));
        Career(new CareerTimelineResponse(true, [], 0));
        Tax(new DisposalTaxSummary(0, 0, 0, null, null, new Dictionary<string, int>()));
        Risk(null);

        var profile = await Read();

        profile.Goals.DataQuality.Should().Be(DataQuality.Missing);
        profile.Goals.Reasons.Should().Equal(FinancialProfileReasons.NoGoals);
        profile.Goals.Data.Should().BeNull();
        profile.Income.Reasons.Should().Equal(FinancialProfileReasons.NoDeclaredIncome);
        profile.Career.Reasons.Should().Equal(FinancialProfileReasons.NoCareerEvents);
        profile.DisposalTax.Reasons.Should().Equal(FinancialProfileReasons.NoPositions);
        profile.Risk.Reasons.Should().Equal(FinancialProfileReasons.NoIps);
        profile.Risk.Data.Should().BeNull();
        profile.MissingSections.Should().Equal("goals", "income", "career", "disposalTax", "risk");
    }

    [Fact]
    public async Task AUserWithNoDataAtAll_HasEverySectionMissing_AndTheIndicatorSuggestsNothing()
    {
        Cashflow(NoHistory());
        Goals(new GoalsProgressResponse(
            GoalsProgressStatus.NoGoals, 100_000m, 1_500m, 6, [], null, new ProjectionRange(0.02m, 0.05m, 0.08m), false));
        Income(IncomeWith(IncomeVariance.NoDeclared));
        Career(new CareerTimelineResponse(true, [], 0));
        Tax(new DisposalTaxSummary(0, 0, 0, null, null, new Dictionary<string, int>()));
        Risk(null);

        var profile = await Read();

        profile.Cashflow.DataQuality.Should().Be(DataQuality.Missing);
        profile.Goals.DataQuality.Should().Be(DataQuality.Missing);
        profile.Income.DataQuality.Should().Be(DataQuality.Missing);
        profile.Career.DataQuality.Should().Be(DataQuality.Missing);
        profile.DisposalTax.DataQuality.Should().Be(DataQuality.Missing);
        profile.Risk.DataQuality.Should().Be(DataQuality.Missing);
        profile.MissingSections.Should().Equal("cashflow", "goals", "income", "career", "disposalTax", "risk");
        profile.AsOf.Should().Be(Now);
        profile.Disclosure.Should().Be(FinancialProfileText.Disclosure);
        profile.CapacityIndicator.Status.Should().Be(CapacityStatus.Insufficient);
        profile.CapacityIndicator.SuggestedBand.Should().BeNull();
    }

    [Fact]
    public async Task UnderThreeCompleteMonths_TheCashflowSectionIsMissing_WithItsOwnReason()
    {
        Cashflow(NoHistory());

        var profile = await Read();

        profile.Cashflow.DataQuality.Should().Be(DataQuality.Missing);
        profile.Cashflow.Reasons.Should().Equal(CashflowProfileReasons.InsufficientMonths);
        profile.MissingSections.Should().Contain("cashflow");
    }

    [Fact]
    public async Task AnUnknownRunway_OrStaleSources_MakeTheCashflowSectionPartial()
    {
        Cashflow(Shown(runway: null, runwayReason: CashflowProfileReasons.NoCashSplit, stale: true));

        var profile = await Read();

        profile.Cashflow.DataQuality.Should().Be(DataQuality.Partial);
        profile.Cashflow.Reasons.Should().Equal(CashflowProfileReasons.NoCashSplit, FinancialProfileReasons.StaleSources);
    }

    [Theory]
    [InlineData(IncomeVariance.DeclaredUnreadable, FinancialProfileReasons.DeclaredUnreadable)]
    [InlineData(IncomeVariance.NotComparable, FinancialProfileReasons.NotComparable)]
    [InlineData(IncomeVariance.NoObserved, FinancialProfileReasons.NoObservedIncome)]
    public async Task IncomeThatCannotBeFullyHeldAgainstTheObserved_IsPartial(IncomeVariance variance, string reason)
    {
        Income(IncomeWith(variance));

        var profile = await Read();

        profile.Income.DataQuality.Should().Be(DataQuality.Partial);
        profile.Income.Reasons.Should().Equal(reason);
    }

    [Fact]
    public async Task CareerEventsThisHostCannotRead_ArePartial()
    {
        Career(new CareerTimelineResponse(false, [Event()], 1));

        var profile = await Read();

        profile.Career.DataQuality.Should().Be(DataQuality.Partial);
        profile.Career.Reasons.Should().Equal(FinancialProfileReasons.CareerUnreadable);
    }

    [Fact]
    public async Task WithheldDisposalTaxPositions_MakeTheSectionPartial_AndAllWithheldMakesItMissing()
    {
        Tax(new DisposalTaxSummary(3, 1, 2, 120m, 2026, new Dictionary<string, int> { ["UNCLASSIFIED"] = 2 }));
        (await Read()).DisposalTax.DataQuality.Should().Be(DataQuality.Partial);

        Tax(new DisposalTaxSummary(3, 0, 3, null, null, new Dictionary<string, int> { ["PROVIDER_NOT_SUPPORTED"] = 3 }));
        var profile = await Read();
        profile.DisposalTax.DataQuality.Should().Be(DataQuality.Missing);
        profile.DisposalTax.Data!.WithheldReasons.Should().ContainKey("PROVIDER_NOT_SUPPORTED");
    }

    [Fact]
    public async Task ARiskCapacityNotSetOrDueForRemeasurement_MakesTheRiskSectionPartial()
    {
        Risk(new IpsRiskProfile(3, null, null, null, true, 20, null));

        var profile = await Read();

        profile.Risk.DataQuality.Should().Be(DataQuality.Partial);
        profile.Risk.Reasons.Should().Equal(
            FinancialProfileReasons.RiskCapacityNotSet, FinancialProfileReasons.DrawdownNotSet, FinancialProfileReasons.RiskRemeasurementDue);
    }

    [Fact]
    public async Task GoalsThatNeedAnInput_MakeTheGoalsSectionPartial()
    {
        Goals(OneDatedGoal(track: GoalTrack.NoTargetDate));

        var profile = await Read();

        profile.Goals.DataQuality.Should().Be(DataQuality.Partial);
        profile.Goals.Reasons.Should().Contain(FinancialProfileReasons.GoalNeedsInput);
    }

    [Fact]
    public async Task ASectionWhoseReadThrows_IsMissingAsUnavailable_AndTheRestStillReturns()
    {
        _tax.Setup(h => h.ReadAsync(UserId, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("broker down"));
        _career.Setup(h => h.Handle(It.IsAny<GetCareerTimelineQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db"));

        var profile = await Read();

        profile.DisposalTax.DataQuality.Should().Be(DataQuality.Missing);
        profile.DisposalTax.Reasons.Should().Equal(FinancialProfileReasons.SectionUnavailable);
        profile.Career.Reasons.Should().Equal(FinancialProfileReasons.SectionUnavailable);
        profile.Cashflow.DataQuality.Should().Be(DataQuality.Complete);
        profile.MissingSections.Should().Contain(["career", "disposalTax"]);
    }

    [Fact]
    public async Task ACancelledRead_IsNotSwallowedAsAMissingSection()
    {
        _tax.Setup(h => h.ReadAsync(UserId, It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        var act = Read;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- the capacity indicator -------------------------------------------------------------------------------

    [Fact]
    public async Task TheIndicator_ReadsTheProfilesOwnFigures_AndSitsBesideTheRecordedCapacity()
    {
        var capacity = (await Read()).CapacityIndicator;

        capacity.Status.Should().Be(CapacityStatus.Suggested);
        capacity.SuggestedBand.Should().Be(5, "30 months of runway, spending at 40% of income, steady income, a goal ten years out");
        capacity.RecordedCapacity.Should().Be(3);
        capacity.DiffersFromRecorded.Should().BeTrue();
        capacity.Factors.Select(f => f.Code).Should().Contain([CapacityIndicator.Runway, CapacityIndicator.GoalHorizon, CapacityIndicator.IncomeSpreadCode]);
    }

    [Fact]
    public async Task TheIndicator_DoesNotDiffer_WhenItMatchesTheRecordedCapacity()
    {
        Risk(new IpsRiskProfile(3, 5, 25m, Now.AddDays(-30), false, 20, null));

        (await Read()).CapacityIndicator.DiffersFromRecorded.Should().BeFalse();
    }

    [Fact]
    public async Task TheIndicator_ReadsTheSpreadOfTheObservedMonths()
    {
        Months(2_000m, 6_000m, 3_000m, 5_000m, 1_000m, 7_000m); // median 4,000; range 6,000 = 1.5 of the median

        var capacity = (await Read()).CapacityIndicator;

        capacity.Factors.Single(f => f.Code == CapacityIndicator.IncomeSpreadCode).Value.Should().Be(1.5m);
        capacity.SuggestedBand.Should().Be(3);
    }

    [Fact]
    public async Task NoCash_IsARunwayOfZero_NotAnUnknownOne()
    {
        Cashflow(new CashflowProfileResponse(
            CashflowProfileStatus.NoCash, 6, "2026-03", "2026-08", 5_000m, 2_000m, 0.4m, null, CashflowProfileReasons.NoCash, null, false));

        var capacity = (await Read()).CapacityIndicator;

        capacity.SuggestedBand.Should().Be(1);
        capacity.Factors.Single(f => f.Code == CapacityIndicator.Runway).Value.Should().Be(0m);
    }

    [Fact]
    public async Task WithoutARunway_TheIndicatorSuggestsNothing()
    {
        Cashflow(NoHistory());

        var capacity = (await Read()).CapacityIndicator;

        capacity.Status.Should().Be(CapacityStatus.Insufficient);
        capacity.SuggestedBand.Should().BeNull();
        capacity.DiffersFromRecorded.Should().BeFalse("there is nothing to differ from");
        capacity.Reason.Should().Be(CapacityIndicator.RunwayUnknown);
    }

    [Fact]
    public async Task IncomeLowerThanDeclared_FeedsTheIndicator()
    {
        Income(IncomeWith(IncomeVariance.ObservedLower));

        var capacity = (await Read()).CapacityIndicator;

        capacity.Factors.Should().Contain(f => f.Code == CapacityIndicator.IncomeBelowDeclared);
        capacity.SuggestedBand.Should().Be(4);
    }

    [Fact]
    public async Task TheNearestDatedGoal_IsTheOneWithTheFewestMonthsLeft_AmongThoseNotYetDue()
    {
        Goals(new GoalsProgressResponse(
            GoalsProgressStatus.Shown, 100_000m, 1_500m, 6,
            [
                new GoalProgress("Passed", 10_000m, new DateOnly(2026, 1, 1), "low", 0, null, null, false, GoalTrack.Behind),
                new GoalProgress("Far", 10_000m, new DateOnly(2040, 1, 1), "low", 170, null, null, false, GoalTrack.OnTrack),
                new GoalProgress("Near", 10_000m, new DateOnly(2027, 5, 1), "high", 6, null, null, false, GoalTrack.OnTrack),
                new GoalProgress("Undated", 10_000m, null, "low", null, null, null, false, GoalTrack.NoTargetDate),
            ],
            null, new ProjectionRange(0.02m, 0.05m, 0.08m), false));

        var profile = await Read();

        profile.Goals.Data!.ShortestGoalMonths.Should().Be(6);
        profile.CapacityIndicator.SuggestedBand.Should().Be(2);
    }
}
