namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using System.Text.Json;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class ScorecardReaderTests
{
    private static readonly OpportunityOptions Opportunity = new();
    private static readonly VerdictOptions VerdictTable = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly ScorecardValuationAxis FairValuation =
        ScorecardValuationAxis.From(26m, 24m, 5, 22m, 100m, 120m, "USD", false);

    [Fact]
    public void Build_LeavesCrowdingUnknown_WhenThereIsNoExtension()
    {
        var composed = Composed(raw: 80, adjusted: 80, fundamentals: 75, extension: null);

        var result = ScorecardReader.Build("MSFT", composed, FairValuation, Opportunity, VerdictTable, Now);

        // The classifier says Normal for a missing extension; the scorecard must not (FR-008).
        composed.Crowding.Should().Be(CrowdingClass.Normal);
        result.Crowding.Class.Should().BeNull();
        result.Crowding.NotEvaluableReason.Should().Be(ScorecardReader.NoExtensionData);
        result.Verdict.FiredRuleId.Should().Be(VerdictRules.Buy);
    }

    [Fact]
    public void Build_PublishesCrowdingAndThresholds_WhenExtensionIsKnown()
    {
        var result = ScorecardReader.Build(
            "MSFT", Composed(80, 80, 75, extension: 0.10m), FairValuation, Opportunity, VerdictTable, Now);

        result.Crowding.Class.Should().Be(nameof(CrowdingClass.Normal));
        result.Crowding.NotEvaluableReason.Should().BeNull();
        result.Crowding.ExtendedExtensionThreshold.Should().Be(Opportunity.ExtendedExtensionThreshold);
        result.Crowding.EarlyExtensionThreshold.Should().Be(Opportunity.EarlyExtensionThreshold);
        result.Verdict.Word.Should().Be(nameof(VerdictWord.StrongBuy));
    }

    [Fact]
    public void Build_VerdictReadsTheAdjustedStructureScore()
    {
        // Raw 75 would be a STRONG BUY; the regime haircut to 65 makes it a BUY (OQ-8).
        var composed = Composed(raw: 75, adjusted: 65, fundamentals: 80, extension: 0.03m, rationale: ["risk_off_haircut"]);

        var result = ScorecardReader.Build("NVDA", composed, FairValuation, Opportunity, VerdictTable, Now);

        result.Structure.RawScore.Should().Be(75);
        result.Structure.AdjustedScore.Should().Be(65);
        result.Verdict.StructureBasis.Should().Be(VerdictDto.AdjustedBasis);
        result.Verdict.Word.Should().Be(nameof(VerdictWord.Buy));
        result.Verdict.Rules.Single(r => r.Id == VerdictRules.StrongBuy).Conditions[0].Actual.Should().Be("65");
    }

    [Fact]
    public void Build_SaysTheBasisIsRaw_WhenThereIsNoRegimeReading()
    {
        var composed = Composed(70, 70, 70, 0.03m, rationale: [RegimeScoreAdjuster.NoRegimeData]);

        var result = ScorecardReader.Build("MSFT", composed, FairValuation, Opportunity, VerdictTable, Now);

        result.Verdict.StructureBasis.Should().Be(VerdictDto.RawBasis);
    }

    [Fact]
    public void Build_MarksOnlyTheFirstMatchedRuleAsFired()
    {
        var result = ScorecardReader.Build(
            "MSFT", Composed(80, 75, 75, extension: 0.03m), FairValuation, Opportunity, VerdictTable, Now);

        result.Verdict.Rules.Should().ContainSingle(r => r.Fired)
            .Which.Id.Should().Be(result.Verdict.FiredRuleId);
        result.Verdict.Rules.Count(r => r.Matched).Should().BeGreaterThan(1);
    }

    [Fact]
    public void Build_NotApplicableValuation_IsNotScored()
    {
        var result = ScorecardReader.Build(
            "BTC", Composed(80, 80, null, 0.03m), ScorecardValuationAxis.NotApplicableAxis, Opportunity, VerdictTable, Now);

        result.Verdict.Word.Should().Be(nameof(VerdictWord.NotScored));
        result.Verdict.FiredRuleId.Should().Be(VerdictRules.OutsideUniverse);
    }

    [Fact]
    public void Scorecard_CarriesNoCompositeNumber()
    {
        var result = ScorecardReader.Build(
            "MSFT", Composed(80, 75, 75, extension: 0.03m), FairValuation, Opportunity, VerdictTable, Now);

        // SC-003 / 019 FR-007: four axes and a word, never a combined score.
        var json = JsonSerializer.Serialize(result).ToLowerInvariant();
        json.Should().NotContainAny("composite", "overall", "weighted", "totalscore");
    }

    [Fact]
    public async Task ReadAsync_SkipsTheFundamentalsRead_WhenValuationDoesNotApply()
    {
        var fundamentals = new CountingFundamentalsService();
        var reader = Reader(fundamentals);

        var result = await reader.ReadAsync("BTC", ScorecardValuationAxis.NotApplicableAxis, null, CancellationToken.None);

        fundamentals.Calls.Should().Be(0);
        result.Verdict.FiredRuleId.Should().Be(VerdictRules.OutsideUniverse);
    }

    [Fact]
    public async Task ReadAsync_ReusesFactsTheCallerAlreadyRead()
    {
        var fundamentals = new CountingFundamentalsService();
        var reader = Reader(fundamentals);

        await reader.ReadAsync("MSFT", FairValuation, [], CancellationToken.None);
        await reader.ReadAsync("MSFT", FairValuation, null, CancellationToken.None);

        fundamentals.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_WithNoDataAnywhere_IsNotScored()
    {
        var reader = Reader(new CountingFundamentalsService());

        var result = await reader.ReadAsync("ZZZZ", ScorecardValuationAxis.Unavailable, null, CancellationToken.None);

        result.Structure.RawScore.Should().BeNull();
        result.Fundamentals.Score.Should().BeNull();
        result.Crowding.Class.Should().BeNull();
        result.Valuation.NotEvaluableReason.Should().Be(ScorecardValuationAxis.ReasonUnavailable);
        result.Verdict.FiredRuleId.Should().Be(VerdictRules.NoAxisData);
    }

    [Fact]
    public void ValuationAxis_ComputesPremiumAndUpside()
    {
        var axis = ScorecardValuationAxis.From(31m, 24m, 5, 28m, 200m, 230m, "USD", false);

        axis.PremiumPct.Should().Be(29.2m);
        axis.ImpliedUpsidePct.Should().Be(15.0m);
        axis.NotEvaluableReason.Should().BeNull();
    }

    [Theory]
    [InlineData(null, 24.0, ScorecardValuationAxis.ReasonNoTrailingPe)]
    [InlineData(-4.0, 24.0, ScorecardValuationAxis.ReasonNoTrailingPe)]
    [InlineData(31.0, null, ScorecardValuationAxis.ReasonNoFiveYearAverage)]
    [InlineData(31.0, 0.0, ScorecardValuationAxis.ReasonNoFiveYearAverage)]
    public void ValuationAxis_NamesWhyThereIsNoPremium(double? trailingPe, double? fiveYearAvg, string reason)
    {
        var axis = ScorecardValuationAxis.From(
            (decimal?)trailingPe, (decimal?)fiveYearAvg, null, null, 100m, null, "USD", false);

        axis.PremiumPct.Should().BeNull();
        axis.ImpliedUpsidePct.Should().BeNull();
        axis.NotEvaluableReason.Should().Be(reason);
    }

    [Fact]
    public void ValuationAxis_FromSnapshot_MapsNullAndNotApplicable()
    {
        ScorecardValuationAxis.From((ValuationSnapshotResult?)null).Should().Be(ScorecardValuationAxis.Unavailable);
        ScorecardValuationAxis.From(ValuationSnapshotResult.ForNonEquity("BTC"))
            .Should().Be(ScorecardValuationAxis.NotApplicableAxis);
    }

    private static ScorecardReader Reader(IFundamentalsService fundamentals) =>
        new(
            new FakeMarketStructureReader(),
            fundamentals,
            new FakeMarketRegimeSource(),
            Options.Create(Opportunity),
            Options.Create(VerdictTable));

    private static ComposedScorecard Composed(
        int? raw, int? adjusted, int? fundamentals, decimal? extension, IReadOnlyList<string>? rationale = null)
    {
        var evidence = ScoreEvidence.Empty with { ExtensionFromMa50 = extension, VolumeRatio = extension is null ? null : 1.0m };
        var crowding = CrowdingClassifier.Classify(extension, evidence.VolumeRatio, Opportunity);
        var regime = new RegimeAdjustment(
            raw, adjusted, (adjusted ?? 0) - (raw ?? 0), rationale ?? ["risk_on"], "Normal", "Normal", false, 15m, 1m, Now);
        return new ComposedScorecard(raw, fundamentals, crowding, evidence, regime);
    }

    private sealed class CountingFundamentalsService : IFundamentalsService
    {
        public int Calls { get; private set; }

        public Task<FundamentalsResult> GetFundamentalsAsync(string ticker, int maxPerConcept, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(FakeFundamentalsService.ResultFor(ticker, []));
        }
    }
}
