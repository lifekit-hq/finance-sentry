namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using System.Text.Json;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
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
    public void Build_CarriesTheCompanyProfile_AsFactsBesideTheAxes()
    {
        var composed = Composed(80, 80, 75, extension: 0.10m);

        var withProfile = ScorecardReader.Build("MSFT", composed, FairValuation, Opportunity, VerdictTable, Now, "Technology", "Software - Infrastructure");
        var without = ScorecardReader.Build("MSFT", composed, FairValuation, Opportunity, VerdictTable, Now);

        withProfile.Sector.Should().Be("Technology");
        withProfile.Industry.Should().Be("Software - Infrastructure");
        without.Sector.Should().BeNull();
        without.Industry.Should().BeNull();
        withProfile.Verdict.Should().BeEquivalentTo(without.Verdict);
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
    public async Task ScorecardQuery_ReadsSectorAndIndustry_FromTheValuationRead()
    {
        var valuation = new Mock<IValuationDataService>();
        valuation.Setup(v => v.GetCurrentMetricsAsync("MSFT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValuationCurrentMetrics(
                "MSFT", 100m, 26m, 22m, null, null, 120m, IsStale: false, NotApplicable: false, "Technology", "Software - Infrastructure"));
        var history = new Mock<IValuationHistoryService>();
        history.Setup(h => h.GetTrailingPeHistoryAsync("MSFT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrailingPeHistory(24m, 5));
        var handler = new GetScorecardQueryHandler(valuation.Object, history.Object, new PeerForwardPeReader(valuation.Object), Reader(new CountingFundamentalsService()));

        var result = await handler.Handle(new GetScorecardQuery(" msft "), CancellationToken.None);

        result.Symbol.Should().Be("MSFT");
        result.Sector.Should().Be("Technology");
        result.Industry.Should().Be("Software - Infrastructure");
    }

    [Fact]
    public async Task ScorecardQuery_HasNoSector_WhenTheValuationIsUnavailableOrNotApplicable()
    {
        var valuation = new Mock<IValuationDataService>();
        valuation.Setup(v => v.GetCurrentMetricsAsync("NOPE", It.IsAny<CancellationToken>())).ReturnsAsync((ValuationCurrentMetrics?)null);
        valuation.Setup(v => v.GetCurrentMetricsAsync("BTC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValuationCurrentMetrics("BTC", null, null, null, null, null, null, false, true, null, null));
        var handler = new GetScorecardQueryHandler(valuation.Object, Mock.Of<IValuationHistoryService>(), new PeerForwardPeReader(valuation.Object), Reader(new CountingFundamentalsService()));

        foreach (var symbol in new[] { "NOPE", "BTC" })
        {
            var result = await handler.Handle(new GetScorecardQuery(symbol), CancellationToken.None);
            result.Sector.Should().BeNull();
            result.Industry.Should().BeNull();
        }
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

    [Fact]
    public void PeerLens_ComparesTheForwardPeWithThePeerMedian_WhenTheOwnHistoryIsMissing()
    {
        var axis = ScorecardValuationAxis.From(26m, null, null, 22m, 100m, 120m, "USD", false);

        var lens = axis.WithPeerLens([20m, 25m, 30m, null, -4m], minPeers: 3);

        lens.PeerForwardPeMedian.Should().Be(25m);
        lens.PeerCount.Should().Be(3);
        lens.PeerPremiumPct.Should().Be(-12.0m);
        lens.PriceLens.Should().Be(PriceLenses.Peers);
        lens.LensPremium().Should().Be(-12.0m);
        lens.PremiumPct.Should().BeNull("the own-history premium stays what it is");
    }

    [Fact]
    public void PeerLens_UsesTheMeanOfTheMiddlePair_ForAnEvenPeerCount()
    {
        var axis = ScorecardValuationAxis.From(null, null, null, 30m, 100m, null, "USD", false);

        axis.WithPeerLens([10m, 20m, 40m, 50m], minPeers: 3).PeerForwardPeMedian.Should().Be(30m);
    }

    [Fact]
    public void PeerLens_RidesBesideTheOwnHistoryAsFacts_AndIsNotWhatTheVerdictReads()
    {
        var withHistory = ScorecardValuationAxis.From(26m, 24m, 5, 22m, 100m, 120m, "USD", false);

        var beside = withHistory.WithPeerLens([20m, 25m, 30m], 3);

        beside.PeerPremiumPct.Should().Be(-12.0m);
        beside.PremiumPct.Should().Be(8.3m);
        beside.PriceLens.Should().Be(PriceLenses.OwnHistory);
        beside.LensPremium().Should().Be(8.3m);
    }

    [Fact]
    public void PeerLens_IsLeftAlone_WhenPeersAreFew_OrTheForwardPeIsMissing()
    {
        var withHistory = ScorecardValuationAxis.From(26m, 24m, 5, 22m, 100m, 120m, "USD", false);
        var noForward = ScorecardValuationAxis.From(26m, null, null, null, 100m, 120m, "USD", false);
        var negativeForward = ScorecardValuationAxis.From(26m, null, null, -3m, 100m, 120m, "USD", false);
        var ok = ScorecardValuationAxis.From(26m, null, null, 22m, 100m, 120m, "USD", false);

        withHistory.WithPeerLens([20m, 25m], 3).Should().Be(withHistory);
        noForward.WithPeerLens([20m, 25m, 30m], 3).Should().Be(noForward);
        negativeForward.WithPeerLens([20m, 25m, 30m], 3).Should().Be(negativeForward);
        ok.WithPeerLens([20m, 25m], 3).Should().Be(ok);
        ok.WithPeerLens([20m, null, -1m, 30m], 3).PriceLens.Should().BeNull("two positive peers are not enough");
        ScorecardValuationAxis.NotApplicableAxis.WithPeerLens([20m, 25m, 30m], 3).Should().Be(ScorecardValuationAxis.NotApplicableAxis);
        withHistory.PriceLens.Should().Be(PriceLenses.OwnHistory);
    }

    [Fact]
    public void Build_ReadsTheRevenueGrowthGate_AndThePeerLens()
    {
        var composed = Composed(80, 80, 75, extension: 0.03m) with
        {
            Evidence = Composed(80, 80, 75, extension: 0.03m).Evidence with { RevenueYoy = 0.096m },
        };
        var peerLens = ScorecardValuationAxis.From(26m, null, null, 22m, 100m, 120m, "USD", false)
            .WithPeerLens([30m, 32m, 34m], 3);

        var slow = ScorecardReader.Build("MSFT", composed, peerLens, Opportunity, VerdictTable, Now);
        var fast = ScorecardReader.Build(
            "MSFT",
            composed with { Evidence = composed.Evidence with { RevenueYoy = 0.12m } },
            peerLens, Opportunity, VerdictTable, Now);

        slow.Verdict.Word.Should().Be(nameof(VerdictWord.Buy));
        fast.Verdict.Word.Should().Be(nameof(VerdictWord.StrongBuy));
        fast.Verdict.PriceLens.Should().Be(PriceLenses.Peers);
        fast.Verdict.RulesVersion.Should().Be(2);
        fast.Verdict.Rules.Single(r => r.Id == VerdictRules.StrongBuy).Conditions
            .Single(c => c.Axis == nameof(VerdictAxis.Valuation)).Actual.Should().Be("-31.2");
        fast.Verdict.Rules.Single(r => r.Id == VerdictRules.StrongBuy).Conditions
            .Single(c => c.Axis == nameof(VerdictAxis.Growth)).Actual.Should().Be("12.0");
    }

    [Fact]
    public void Build_NamesTheReasonOfAWait_OnTheVerdict()
    {
        var result = ScorecardReader.Build("MSFT", Composed(45, 45, 85, extension: 0.03m), FairValuation, Opportunity, VerdictTable, Now);

        result.Verdict.Word.Should().Be(nameof(VerdictWord.Wait));
        result.Verdict.FiredRuleId.Should().Be(VerdictRules.StrongBusinessWeakTrend);
        result.Verdict.WaitReason.Should().Be(WaitReasons.StrongBusinessWeakTrend);
        result.Verdict.WaitReasonLabel.Should().Be("Strong business, weak trend");
        ScorecardReader.Build("MSFT", Composed(80, 80, 75, extension: 0.03m), FairValuation, Opportunity, VerdictTable, Now)
            .Verdict.WaitReasonLabel.Should().BeNull();
    }

    [Fact]
    public async Task ScorecardQuery_FallsBackToThePeerForwardPe_OnlyWhenTheFiveYearHistoryIsMissing()
    {
        var valuation = new Mock<IValuationDataService>();
        valuation.Setup(v => v.GetCurrentMetricsAsync("NEW", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Metrics("NEW", forwardPe: 20m));
        valuation.Setup(v => v.GetPeerSymbolsAsync("NEW", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["P1", "P2", "P3", "NEW"]);
        foreach (var (symbol, pe) in new[] { ("P1", 24m), ("P2", 25m), ("P3", 26m) })
        {
            valuation.Setup(v => v.GetCurrentMetricsAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Metrics(symbol, forwardPe: pe));
        }

        var history = new Mock<IValuationHistoryService>();
        history.Setup(h => h.GetTrailingPeHistoryAsync("NEW", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrailingPeHistory(null, null));
        var handler = new GetScorecardQueryHandler(
            valuation.Object, history.Object, new PeerForwardPeReader(valuation.Object), Reader(new CountingFundamentalsService()));

        var result = await handler.Handle(new GetScorecardQuery("new"), CancellationToken.None);

        result.Valuation.PremiumPct.Should().BeNull();
        result.Valuation.PeerPremiumPct.Should().Be(-20.0m);
        result.Valuation.PeerCount.Should().Be(3);
        result.Valuation.PriceLens.Should().Be(PriceLenses.Peers);
        result.Verdict.PriceLens.Should().Be(PriceLenses.Peers);
        valuation.Verify(v => v.GetCurrentMetricsAsync("NEW", It.IsAny<CancellationToken>()), Times.Once);

        // With a five-year history the peers are never asked for.
        history.Setup(h => h.GetTrailingPeHistoryAsync("NEW", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrailingPeHistory(24m, 5));
        valuation.Invocations.Clear();
        var own = await handler.Handle(new GetScorecardQuery("NEW"), CancellationToken.None);

        own.Valuation.PriceLens.Should().Be(PriceLenses.OwnHistory);
        own.Valuation.PeerPremiumPct.Should().BeNull();
        valuation.Verify(v => v.GetPeerSymbolsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ValuationCurrentMetrics Metrics(string ticker, decimal forwardPe) =>
        new(ticker, 100m, 26m, forwardPe, null, null, 120m, IsStale: false, NotApplicable: false, "Technology", "Software");

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
        var evidence = ScoreEvidence.Empty with { ExtensionFromMa50 = extension, VolumeRatio = extension is null ? null : 1.0m, RevenueYoy = 0.15m };
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
