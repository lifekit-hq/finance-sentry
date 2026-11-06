namespace FinanceSentry.Tests.Unit.Core.Measures;

using FinanceSentry.Core.Measures;
using FluentAssertions;
using Xunit;

/// <summary>The scenario path (#454 S5): dated events turned into month-by-month changes, and the projections that take them. Synthetic figures.</summary>
public sealed class ScenarioProjectionTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);
    private static readonly ProjectionAssumptions Zero = new(0m, 0.04m);
    private static readonly ProjectionAssumptions Default = ProjectionAssumptions.Default;

    private static ScenarioEvent Step(DateOnly date, decimal monthly) => new(ScenarioEventKind.IncomeStep, date, null, monthly);

    [Fact]
    public void MonthlyDeltas_AnIncomeStep_ChangesEveryMonthFromTheMonthOfItsDate()
    {
        var deltas = ScenarioPath.MonthlyDeltas([Step(new DateOnly(2026, 12, 1), 500m)], Today, 6);

        // 26 Sep -> 1 Dec is two whole months, so it lands in the third month (index 2) and stays.
        deltas.Should().Equal(0m, 0m, 500m, 500m, 500m, 500m);
    }

    [Fact]
    public void MonthlyDeltas_AnEventTenDaysOut_LandsInTheFirstMonth()
    {
        var deltas = ScenarioPath.MonthlyDeltas([Step(new DateOnly(2026, 10, 6), -300m)], Today, 3);

        deltas.Should().Equal(-300m, -300m, -300m);
    }

    [Fact]
    public void MonthlyDeltas_OneOffs_LandInOneMonthEach_AndACostSubtracts()
    {
        var deltas = ScenarioPath.MonthlyDeltas(
            [
                new ScenarioEvent(ScenarioEventKind.OneOffIn, new DateOnly(2026, 11, 30), null, 2_000m),
                new ScenarioEvent(ScenarioEventKind.OneOffOut, new DateOnly(2027, 1, 15), null, 900m),
            ],
            Today,
            6);

        deltas.Should().Equal(0m, 0m, 2_000m, -900m, 0m, 0m);
    }

    [Fact]
    public void MonthlyDeltas_ALeave_LowersTheSavingForItsMonthsOnly()
    {
        var leave = new ScenarioEvent(ScenarioEventKind.Leave, new DateOnly(2026, 12, 1), new DateOnly(2027, 2, 28), 1_200m);

        var deltas = ScenarioPath.MonthlyDeltas([leave], Today, 8);

        deltas.Should().Equal(0m, 0m, -1_200m, -1_200m, -1_200m, 0m, 0m, 0m);
    }

    [Theory]
    [InlineData("2026-12-01", "2026-12-20", 1)]
    [InlineData("2026-12-01", "2027-02-28", 3)]
    [InlineData("2026-12-15", "2027-03-14", 3)]
    [InlineData("2026-12-01", "2027-12-31", 13)]
    public void LeaveMonths_IsItsLengthInWholeMonths_AndAtLeastOne(string start, string end, int expected)
        => ScenarioPath.LeaveMonths(DateOnly.Parse(start), DateOnly.Parse(end)).Should().Be(expected);

    [Fact]
    public void MonthlyDeltas_AnEventAlreadyInThePast_IsNotCounted_BecauseTheBaselineHasIt()
    {
        var deltas = ScenarioPath.MonthlyDeltas(
            [Step(new DateOnly(2026, 9, 26), 500m), Step(new DateOnly(2026, 1, 1), 900m)], Today, 4);

        deltas.Should().OnlyContain(d => d == 0m);
    }

    [Fact]
    public void MonthlyDeltas_PastTheHorizon_AddsNothing_AndALeaveEndingBeforeItStartsIsIgnored()
    {
        var deltas = ScenarioPath.MonthlyDeltas(
            [
                Step(new DateOnly(2030, 1, 1), 500m),
                new ScenarioEvent(ScenarioEventKind.Leave, new DateOnly(2026, 12, 1), new DateOnly(2026, 11, 1), 1_000m),
            ],
            Today,
            6);

        deltas.Should().OnlyContain(d => d == 0m);
    }

    [Fact]
    public void ValueAfter_WithNoDeltas_IsTheBaseline()
    {
        var scenario = Projection.ValueAfter(12, 10_000m, 1_000m, Default, new decimal[12]);

        scenario.Should().Be(Projection.ValueAfter(12, 10_000m, 1_000m, Default));
    }

    [Fact]
    public void ValueAfter_AtZeroReturn_AddsTheDeltasUp()
    {
        var deltas = ScenarioPath.MonthlyDeltas(
            [Step(new DateOnly(2026, 12, 1), 500m), new ScenarioEvent(ScenarioEventKind.OneOffOut, new DateOnly(2027, 1, 15), null, 900m)],
            Today,
            12);

        var scenario = Projection.ValueAfter(12, 10_000m, 1_000m, Zero, deltas);

        // 10,000 + 12 x 1,000 + 10 months of +500 - 900.
        scenario.Central.Should().Be(10_000m + 12_000m + 5_000m - 900m);
        scenario.Low.Should().Be(scenario.Central, "a zero real return has no spread above it, and the low bound is floored at zero");
    }

    [Fact]
    public void ValueAfter_ARaiseLifts_AndACutLowers_TheCentralFigure_AboveAndBelowTheBaseline()
    {
        var baseline = Projection.ValueAfter(12, 10_000m, 1_000m, Default).Central;

        var raised = Projection.ValueAfter(12, 10_000m, 1_000m, Default, ScenarioPath.MonthlyDeltas([Step(new DateOnly(2026, 11, 1), 400m)], Today, 12));
        var cut = Projection.ValueAfter(12, 10_000m, 1_000m, Default, ScenarioPath.MonthlyDeltas([Step(new DateOnly(2026, 11, 1), -400m)], Today, 12));

        raised.Central.Should().BeGreaterThan(baseline);
        cut.Central.Should().BeLessThan(baseline);
        (raised.Central - baseline).Should().BeApproximately(baseline - cut.Central, 0.01m, "the future value is linear in the deltas");
        raised.Low.Should().BeLessThanOrEqualTo(raised.Central);
        raised.Central.Should().BeLessThanOrEqualTo(raised.High);
    }

    [Fact]
    public void ValueAfter_ADeltaPastTheMonths_IsIgnored()
    {
        var scenario = Projection.ValueAfter(3, 0m, 100m, Zero, [0m, 0m, 0m, 9_999m]);

        scenario.Central.Should().Be(300m);
    }

    [Fact]
    public void RequiredMonthlySaving_CountsTheEvents_AndNeverGoesNegative()
    {
        var plain = Projection.RequiredMonthlySaving(12, 30_000m, 10_000m, Zero);
        var bonus = ScenarioPath.MonthlyDeltas([new ScenarioEvent(ScenarioEventKind.OneOffIn, new DateOnly(2026, 12, 1), null, 6_000m)], Today, 12);
        var huge = ScenarioPath.MonthlyDeltas([new ScenarioEvent(ScenarioEventKind.OneOffIn, new DateOnly(2026, 12, 1), null, 50_000m)], Today, 12);

        plain.Central.Should().BeApproximately(20_000m / 12m, 0.01m);
        Projection.RequiredMonthlySaving(12, 30_000m, 10_000m, Zero, bonus).Central.Should().BeApproximately(14_000m / 12m, 0.01m);
        Projection.RequiredMonthlySaving(12, 30_000m, 10_000m, Zero, huge).Should().Be(new ProjectionRange(0m, 0m, 0m));
    }

    [Fact]
    public void RequiredMonthlySaving_WithNoDeltas_IsTheBaseline()
    {
        Projection.RequiredMonthlySaving(24, 50_000m, 5_000m, Default, new decimal[24])
            .Should().Be(Projection.RequiredMonthlySaving(24, 50_000m, 5_000m, Default));
    }

    [Fact]
    public void FinancialIndependence_WithAStepUp_ArrivesEarlierThanTheBaseline()
    {
        var deltas = ScenarioPath.MonthlyDeltas([Step(new DateOnly(2026, 11, 1), 1_000m)], Today, Projection.MaxScenarioMonths);

        var baseline = Projection.FinancialIndependence(24_000m, 100_000m, 1_000m, Default);
        var scenario = Projection.FinancialIndependence(24_000m, 100_000m, 1_000m, Default, deltas);

        baseline.Status.Should().Be(FireReachability.Reachable);
        scenario.Status.Should().Be(FireReachability.Reachable);
        scenario.TargetUsd.Should().Be(baseline.TargetUsd);
        scenario.MonthsToTarget!.Value.Central.Should().BeLessThan(baseline.MonthsToTarget!.Value.Central);
        scenario.MonthsToTarget.Value.Low.Should().BeLessThanOrEqualTo(scenario.MonthsToTarget.Value.Central);
        scenario.MonthsToTarget.Value.Central.Should().BeLessThanOrEqualTo(scenario.MonthsToTarget.Value.High);
    }

    [Fact]
    public void FinancialIndependence_WalkMatchesTheClosedFormBaseline_WhenThereAreNoDeltas()
    {
        var baseline = Projection.FinancialIndependence(24_000m, 100_000m, 1_000m, Default).MonthsToTarget!.Value;
        var walked = Projection.FinancialIndependence(24_000m, 100_000m, 1_000m, Default, new decimal[Projection.MaxScenarioMonths]).MonthsToTarget!.Value;

        walked.Central.Should().BeApproximately(baseline.Central, 0.5m, "the walk interpolates the crossing month");
    }

    [Fact]
    public void FinancialIndependence_AlreadyReached_NeedsNoWalk()
    {
        var scenario = Projection.FinancialIndependence(24_000m, 700_000m, -500m, Default, [-100m]);

        scenario.Status.Should().Be(FireReachability.AlreadyReached);
        scenario.MonthsToTarget.Should().Be(new ProjectionRange(0m, 0m, 0m));
    }

    [Fact]
    public void FinancialIndependence_ARaiseCanTurnANonSavingBaselineIntoADate()
    {
        var deltas = ScenarioPath.MonthlyDeltas([Step(new DateOnly(2026, 11, 1), 1_500m)], Today, Projection.MaxScenarioMonths);

        Projection.FinancialIndependence(24_000m, 100_000m, -200m, Default).Status.Should().Be(FireReachability.NotSaving);
        var scenario = Projection.FinancialIndependence(24_000m, 100_000m, -200m, Default, deltas);

        scenario.Status.Should().Be(FireReachability.Reachable);
        scenario.MonthsToTarget!.Value.Central.Should().BePositive();
    }

    [Fact]
    public void FinancialIndependence_ACutThatNeverRecovers_IsNotWithinTheHorizon_WithNoInvention()
    {
        var deltas = ScenarioPath.MonthlyDeltas([Step(new DateOnly(2026, 11, 1), -2_000m)], Today, Projection.MaxScenarioMonths);

        var scenario = Projection.FinancialIndependence(240_000m, 1_000m, 500m, Zero, deltas);

        scenario.Status.Should().Be(FireReachability.NotWithinHorizon);
        scenario.MonthsToTarget.Should().BeNull();
    }
}
