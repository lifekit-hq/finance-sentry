namespace FinanceSentry.Tests.Unit.Core.Headlines;

using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Measures;
using FinanceSentry.Core.Utils;
using FluentAssertions;
using Xunit;

/// <summary>
/// The finance-framework §3.6 mapping table: each of today's signals becomes the quality, basis status and typed
/// reasons the table names. One test per row, and one per reason code that can arise today.
/// </summary>
public class HeadlineQualityMapperTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Fact]
    public void NoSignals_IsComplete_WithNoReasons()
    {
        var headline = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today, HeadlineSignals.None);

        headline.Quality.Should().Be(HeadlineQuality.Complete);
        headline.Reasons.Should().BeEmpty();
        headline.Amount.Should().Be(1000m);
        headline.Percent.Should().BeNull();
        headline.Method.Should().Be(HeadlineMethod.Value);
        headline.Currency.Should().Be("USD");
    }

    // Row: StaleSleeves -> DEGRADED + STALE_SOURCE per source.
    [Fact]
    public void StaleSleeves_AreDegraded_WithOneStaleSourcePerSource()
    {
        var signals = HeadlineSignals.FromSnapshot("crypto,brokerage", isApproximate: false, recordedFx: FxStatus.Exact);

        var headline = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today, signals);

        headline.Quality.Should().Be(HeadlineQuality.Degraded);
        headline.Reasons.Should().Equal(
            new HeadlineReason(HeadlineReasonCode.StaleSource, "brokerage"),
            new HeadlineReason(HeadlineReasonCode.StaleSource, "crypto"));
        headline.Amount.Should().Be(1000m);
    }

    // Row: IsApproximate -> ESTIMATED + BACKFILLED_ESTIMATE + FX_TODAY_RATE.
    [Fact]
    public void ApproximateSnapshot_IsEstimated_WithBackfilledEstimateAndTodayRate()
    {
        var signals = HeadlineSignals.FromSnapshot(null, isApproximate: true, recordedFx: (FxStatus?)null);

        var headline = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today, signals);

        headline.Quality.Should().Be(HeadlineQuality.Estimated);
        headline.Reasons.Select(r => r.Code).Should().Equal(
            HeadlineReasonCode.BackfilledEstimate, HeadlineReasonCode.FxTodayRate);
    }

    [Fact]
    public void ApproximateSnapshot_ValuedAtAnExactRate_StillCarriesTodayRate()
    {
        // A reconstructed day is valued at today's rate whatever the row recorded (money-semantics §8).
        var signals = HeadlineSignals.FromSnapshot(null, isApproximate: true, recordedFx: FxStatus.Exact);

        var headline = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today, signals);

        headline.Reasons.Select(r => r.Code).Should().Contain(HeadlineReasonCode.FxTodayRate);
    }

    // Row: historical FX replaced by today's rate -> ESTIMATED + FX_TODAY_RATE.
    [Theory]
    [InlineData(FxStatus.TodayRate)]
    [InlineData(FxStatus.SeedTable)]
    public void TodayRateConversion_IsEstimated_WithFxTodayRate(FxStatus status)
    {
        var headline = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today, new HeadlineSignals { Fx = status });

        headline.Quality.Should().Be(HeadlineQuality.Estimated);
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.FxTodayRate));
    }

    [Theory]
    [InlineData(FxStatus.Exact)]
    [InlineData(FxStatus.NearestPublished)]
    public void PublishedRateConversion_NeedsNoFxReason(FxStatus status)
    {
        var headline = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today, new HeadlineSignals { Fx = status });

        headline.Quality.Should().Be(HeadlineQuality.Complete);
        headline.Reasons.Should().BeEmpty();
    }

    // Row: unknown currency 1:1 -> DEGRADED + FX_UNKNOWN_1TO1.
    [Fact]
    public void UnknownCurrency_IsDegraded_WithFxUnknown1To1()
    {
        var headline = HeadlineFactory.Value(
            1000m, HeadlineScope.Umbrella, Today, new HeadlineSignals { Fx = FxStatus.Unknown1To1 });

        headline.Quality.Should().Be(HeadlineQuality.Degraded);
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.FxUnknown1To1));
    }

    [Fact]
    public void HistoryRowFxReasons_MapBackToTheirStatus()
    {
        HeadlineSignals.FromSnapshot(null, false, ["FX_UNKNOWN_1TO1"]).Fx.Should().Be(FxStatus.Unknown1To1);
        HeadlineSignals.FromSnapshot(null, false, ["FX_TODAY_RATE"]).Fx.Should().Be(FxStatus.TodayRate);
        HeadlineSignals.FromSnapshot(null, false, Array.Empty<string>()).Fx.Should().Be(FxStatus.Exact);
    }

    // Row: CashTotal null -> NO_SPLIT; the cash and invested splits are UNAVAILABLE.
    [Fact]
    public void WithheldSplit_IsUnavailable_WithNoSplit_AndNoAmount()
    {
        var headline = HeadlineFactory.Value(
            540m, HeadlineScope.CashSleeve, Today, new HeadlineSignals { NoSplit = true });

        headline.Amount.Should().BeNull();
        headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.NoSplit));
    }

    [Fact]
    public void MissingAmount_IsUnavailable_WhateverItsReasons()
    {
        var headline = HeadlineFactory.Value(null, HeadlineScope.Umbrella, Today, HeadlineSignals.None);

        headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        headline.Amount.Should().BeNull();
    }

    [Fact]
    public void Quality_IsTheWorstReason()
    {
        var signals = new HeadlineSignals { StaleSources = ["ibkr"], Fx = FxStatus.TodayRate };

        var headline = HeadlineFactory.Value(1000m, HeadlineScope.Umbrella, Today, signals);

        headline.Quality.Should().Be(HeadlineQuality.Degraded);
        headline.Reasons.Select(r => r.Code).Should().Equal(HeadlineReasonCode.StaleSource, HeadlineReasonCode.FxTodayRate);
    }

    // Row: SCOPE_CHANGED (R3) -> the change keeps its amount and withholds its percent.
    [Fact]
    public void ScopeChange_KeepsTheAmount_WithholdsThePercent()
    {
        var headline = HeadlineFactory.Change(
            1000m, 1500m, HeadlineScope.Umbrella, Today.AddDays(-30), Today, scopeChanged: true, HeadlineSignals.None);

        headline.Amount.Should().Be(500m);
        headline.Percent.Should().BeNull();
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.ScopeChanged));
        headline.Quality.Should().Be(HeadlineQuality.Complete);
    }

    // D5: a zero base has no percent and says so, never 0%.
    [Fact]
    public void ZeroStart_WithholdsThePercent_WithZeroBase()
    {
        var headline = HeadlineFactory.Change(
            0m, 1500m, HeadlineScope.InvestedBrokerage, Today.AddDays(-30), Today, false, HeadlineSignals.None);

        headline.Amount.Should().Be(1500m);
        headline.Percent.Should().BeNull();
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.ZeroBase));
    }

    [Fact]
    public void NegativeStart_WithholdsThePercent_WithNegativeBase()
    {
        var headline = HeadlineFactory.Change(
            -200m, 100m, HeadlineScope.Umbrella, Today.AddDays(-30), Today, false, HeadlineSignals.None);

        headline.Amount.Should().Be(300m);
        headline.Percent.Should().BeNull();
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.NegativeBase));
    }

    // Row: "Book N/A" -> UNAVAILABLE + SHORT_HISTORY; D5: a missing basis is null, not 0.
    [Fact]
    public void MissingStart_IsUnavailable_WithShortHistory_AndMissingBasis()
    {
        var headline = HeadlineFactory.Change(
            null, 1500m, HeadlineScope.InvestedBrokerage, Today.AddDays(-30), Today, false, HeadlineSignals.None);

        headline.Amount.Should().BeNull();
        headline.Percent.Should().BeNull();
        headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        headline.BasisStatus.Should().Be(BasisStatus.Missing);
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.ShortHistory));
    }

    [Fact]
    public void Change_SharesItsNumeratorBetweenAmountAndPercent()
    {
        var headline = HeadlineFactory.Change(
            1000m, 1050m, HeadlineScope.Umbrella, Today.AddDays(-30), Today, false, HeadlineSignals.None);

        headline.Amount.Should().Be(50m);
        headline.Percent.Should().Be(5.00m);
        headline.Method.Should().Be(HeadlineMethod.ValueChange);
        headline.Basis.Should().Be(HeadlineBasis.StartValue);
        headline.BasisStatus.Should().Be(BasisStatus.Complete);
        headline.Window.Should().Be(new HeadlineWindow(Today.AddDays(-30), Today));
    }

    // Row: PositionReturns.Coverage -> basisStatus COMPLETE / PARTIAL / MISSING.
    [Theory]
    [InlineData("complete", BasisStatus.Complete)]
    [InlineData("partial", BasisStatus.Partial)]
    [InlineData("unavailable", BasisStatus.Missing)]
    public void PositionReturnsCoverage_MapsOntoBasisStatus(string coverage, BasisStatus expected)
    {
        HeadlineQualityMapper.BasisStatusFromCoverage(coverage).Should().Be(expected);
    }

    [Fact]
    public void GainVsCost_OverPartOfTheBook_NamesTheMeasuredShare()
    {
        CostedValue[] positions = [new(600m, 500m), new(400m, null)];

        var headline = HeadlineFactory.GainVsCost(positions, HeadlineScope.InvestedSleeve, Today, HeadlineSignals.None);

        headline.Amount.Should().Be(100m);
        headline.Percent.Should().Be(20.00m);
        headline.Method.Should().Be(HeadlineMethod.GainVsCost);
        headline.Basis.Should().Be(HeadlineBasis.Cost);
        headline.BasisStatus.Should().Be(BasisStatus.Partial);
        headline.Coverage.Should().Be(0.6m);
        headline.Quality.Should().Be(HeadlineQuality.Complete);
    }

    [Fact]
    public void GainVsCost_WithNoKnownBasis_IsUnavailable_NeverZeroPercent()
    {
        CostedValue[] positions = [new(600m, null), new(400m, 0m)];

        var headline = HeadlineFactory.GainVsCost(positions, HeadlineScope.InvestedSleeve, Today, HeadlineSignals.None);

        headline.Amount.Should().BeNull();
        headline.Percent.Should().BeNull();
        headline.BasisStatus.Should().Be(BasisStatus.Missing);
        headline.Coverage.Should().Be(0m);
        headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.NoCostBasis));
    }

    [Fact]
    public void AFigureWithNoAmount_AndNoSignals_IsUnavailable_WithNotMeasurable()
    {
        var headline = HeadlineFactory.Value(null, HeadlineScope.Umbrella, Today, HeadlineSignals.None);

        headline.Amount.Should().BeNull();
        headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.NotMeasurable));
    }

    [Fact]
    public void AFigureWithNoAmount_KeepsItsOwnReason_InsteadOfNotMeasurable()
    {
        var headline = HeadlineFactory.Value(1000m, HeadlineScope.CashSleeve, Today, new HeadlineSignals { NoSplit = true });

        headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.NoSplit));
    }

    [Theory]
    [InlineData(MeasureFailure.ZeroBase, HeadlineReasonCode.ZeroBase)]
    [InlineData(MeasureFailure.NegativeBase, HeadlineReasonCode.NegativeBase)]
    [InlineData(MeasureFailure.ScopeChanged, HeadlineReasonCode.ScopeChanged)]
    [InlineData(MeasureFailure.MissingValue, HeadlineReasonCode.ShortHistory)]
    [InlineData(MeasureFailure.NoSignChange, HeadlineReasonCode.NotMeasurable)]
    public void MeasureFailure_MapsOntoAReason(MeasureFailure failure, HeadlineReasonCode expected)
    {
        HeadlineQualityMapper.ReasonFor(failure).Should().Be(expected);
    }

    [Fact]
    public void Signals_Merge_KeepsTheWorstOfBothEnds()
    {
        var start = HeadlineSignals.FromSnapshot("crypto", isApproximate: true, recordedFx: FxStatus.TodayRate);
        var end = new HeadlineSignals { StaleSources = ["Crypto", "brokerage"], Fx = FxStatus.Unknown1To1 };

        var merged = start.Merge(end);

        merged.StaleSources.Should().BeEquivalentTo(["crypto", "brokerage"]);
        merged.BackfilledEstimate.Should().BeTrue();
        merged.Fx.Should().Be(FxStatus.Unknown1To1);
    }

    // Row: UnknownFlowSources -> UNKNOWN_FLOW per source; a growth figure cannot be split, so it is UNAVAILABLE.
    [Fact]
    public void UnknownFlowSources_MakeAGrowthFigureUnavailable_WithOneUnknownFlowPerSource()
    {
        var signals = HeadlineSignals.None with { UnknownFlowSources = ["inzhur", "ibkr"] };

        var headline = HeadlineFactory.Growth(
            110m, MeasuredRate.Of(0.1m), HeadlineScope.Sleeve("brokerage"), Today.AddDays(-30), Today, signals);

        headline.Quality.Should().Be(HeadlineQuality.Unavailable);
        headline.Amount.Should().BeNull();
        headline.Percent.Should().BeNull();
        headline.BasisStatus.Should().Be(BasisStatus.Missing);
        headline.Reasons.Should().Equal(
            new HeadlineReason(HeadlineReasonCode.UnknownFlow, "ibkr"),
            new HeadlineReason(HeadlineReasonCode.UnknownFlow, "inzhur"));
    }

    // Row: UnknownFlowSources on a figure that still has an amount -> ESTIMATED.
    [Fact]
    public void UnknownFlowSources_OnAnAvailableFigure_AreEstimated()
    {
        var assessment = HeadlineQualityMapper.Assess(
            HeadlineSignals.None with { UnknownFlowSources = ["ibkr"] }, amountAvailable: true);

        assessment.Quality.Should().Be(HeadlineQuality.Estimated);
        assessment.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.UnknownFlow, "ibkr"));
    }

    // Row: ScopeChanged (bridged) -> COMPLETE + SCOPE_CHANGED, informative.
    [Fact]
    public void ScopeChanged_IsInformative_OnABridgedGrowthFigure()
    {
        var signals = HeadlineSignals.None with { ScopeChanged = true };

        var headline = HeadlineFactory.Growth(
            110m, MeasuredRate.Of(0.1m), HeadlineScope.Sleeve("brokerage"), Today.AddDays(-30), Today, signals);

        headline.Quality.Should().Be(HeadlineQuality.Complete);
        headline.Amount.Should().Be(110m);
        headline.Percent.Should().Be(10m);
        headline.Method.Should().Be(HeadlineMethod.ModifiedDietz);
        headline.Basis.Should().Be(HeadlineBasis.AverageCapital);
        headline.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.ScopeChanged));
    }
}

