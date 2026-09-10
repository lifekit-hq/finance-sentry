namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class ScoreCandidateHandlerTests
{
    private static ScoreCandidateCommandHandler BuildHandler(
        FakeCandidateRepository candidates,
        FakeCandidateScoreRepository scores,
        InvestmentPolicyStatement? ips = null,
        decimal? maxPositionCap = null,
        IReadOnlyList<BrokerageHoldingSummary>? holdings = null,
        IReadOnlyList<FundamentalFact>? facts = null,
        MarketStructureSnapshot? structure = null,
        MarketRegimeSnapshot? regime = null)
        => new(
            candidates,
            scores,
            new FakeMarketStructureReader(structure),
            new FakeSecEdgarService(facts),
            new FakeIpsRepository(ips),
            new FakePositionCapSource(maxPositionCap),
            new FakeBrokerageHoldingsReader(holdings),
            new RecordingRadarSignalWriter(),
            new FakeMarketRegimeSource(regime),
            new RecordingThesisEventRecorder(),
            new FakeOpportunityAlertGenerator(),
            Options.Create(new OpportunityOptions()));

    [Fact]
    public async Task Handle_ReScore_AppendsCandidateScore_WithoutDuplicatingCandidate()
    {
        var candidates = new FakeCandidateRepository();
        var scores = new FakeCandidateScoreRepository();
        var handler = BuildHandler(candidates, scores);
        var userId = Guid.NewGuid();

        var first = await handler.Handle(new ScoreCandidateCommand(userId, "MSFT"), CancellationToken.None);
        var second = await handler.Handle(new ScoreCandidateCommand(userId, "MSFT"), CancellationToken.None);

        first.IsNewCandidate.Should().BeTrue();
        second.IsNewCandidate.Should().BeFalse();
        second.CandidateId.Should().Be(first.CandidateId);

        candidates.Candidates.Should().ContainSingle();
        scores.Scores.Count(s => s.CandidateId == first.CandidateId).Should().Be(2);
    }

    [Fact]
    public async Task Handle_FlagsIpsConcentration_WhenHeldTickerExceedsMaxSinglePosition()
    {
        var userId = Guid.NewGuid();
        // 039: an IPS exists (so IpsFit is evaluated) and the position cap now comes from the Risk
        // rule set — a fraction — via IPositionCapSource. Fraction-vs-fraction comparison.
        var ips = new InvestmentPolicyStatement { UserId = userId };
        var holdings = new List<BrokerageHoldingSummary>
        {
            new("MSFT", "STK", 100m, 8_000m, DateTime.UtcNow, "ibkr"),
            new("AAPL", "STK", 100m, 2_000m, DateTime.UtcNow, "ibkr"),
        };

        var handler = BuildHandler(
            new FakeCandidateRepository(), new FakeCandidateScoreRepository(), ips, maxPositionCap: 0.10m, holdings: holdings);
        var result = await handler.Handle(new ScoreCandidateCommand(userId, "MSFT"), CancellationToken.None);

        // MSFT is 80% of the $10k book vs a 10% cap → concentration flag trips.
        result.Scorecard.IpsFit.CurrentWeight.Should().Be(0.80m);
        result.Scorecard.IpsFit.MaxSinglePositionPct.Should().Be(0.10m);
        result.Scorecard.IpsFit.WithinConcentration.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithinConcentration_WhenCapAbsent_FromRiskPort()
    {
        var userId = Guid.NewGuid();
        var ips = new InvestmentPolicyStatement { UserId = userId };
        var holdings = new List<BrokerageHoldingSummary>
        {
            new("MSFT", "STK", 100m, 8_000m, DateTime.UtcNow, "ibkr"),
            new("AAPL", "STK", 100m, 2_000m, DateTime.UtcNow, "ibkr"),
        };

        // 039/FR-007: no cap on file → permissive, exactly as before the repoint.
        var handler = BuildHandler(
            new FakeCandidateRepository(), new FakeCandidateScoreRepository(), ips, maxPositionCap: null, holdings: holdings);
        var result = await handler.Handle(new ScoreCandidateCommand(userId, "MSFT"), CancellationToken.None);

        result.Scorecard.IpsFit.CurrentWeight.Should().Be(0.80m);
        result.Scorecard.IpsFit.MaxSinglePositionPct.Should().BeNull();
        result.Scorecard.IpsFit.WithinConcentration.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_LeavesSubScoresNull_WhenNoStructureOrFundamentalsData()
    {
        var handler = BuildHandler(new FakeCandidateRepository(), new FakeCandidateScoreRepository());

        var result = await handler.Handle(
            new ScoreCandidateCommand(Guid.NewGuid(), "MSFT"), CancellationToken.None);

        // Not-evaluable is labeled null, never faked to a neutral number (FR-002/FR-006).
        result.Scorecard.StructureScore.Should().BeNull();
        result.Scorecard.FundamentalsScore.Should().BeNull();
    }

    // 021: a Panic/Inverted regime haircuts the regime-adjusted structure score of an Extended
    // candidate while the RAW (persisted) structure score is preserved. Regime is context, not action.
    [Fact]
    public async Task Handle_PanicInvertedRegime_HaircutsExtendedCandidate_RawScorePreserved()
    {
        var userId = Guid.NewGuid();
        // Extended crowding: extension >= 0.20 and volume >= 1.5; plus RS so a structure score exists.
        var structure = new MarketStructureSnapshot(
            "MSFT",
            new Dictionary<int, decimal?> { [21] = 0.1m },
            new Dictionary<int, decimal?> { [21] = 0.1m },
            ExtensionFromMa50: 0.25m,
            TodayZScore: 1.0m,
            VolumeRatio: 2.0m,
            Ma50: 100m,
            Ma200: 90m,
            Stale: false);

        var regime = new MarketRegimeSnapshot(
            DateTimeOffset.UtcNow,
            true, "Panic", 34m, "Rising",
            true, "Inverted", -0.3m, true, "quality/defensive (recession-warning)",
            null, null);

        var scores = new FakeCandidateScoreRepository();
        var handler = BuildHandler(
            new FakeCandidateRepository(), scores, structure: structure, regime: regime);
        var result = await handler.Handle(new ScoreCandidateCommand(userId, "MSFT"), CancellationToken.None);

        result.Scorecard.Crowding.Should().Be(FinanceSentry.Modules.Research.Domain.Opportunity.CrowdingClass.Extended);
        result.Scorecard.Regime.Should().NotBeNull();
        result.Scorecard.Regime!.AdjustmentPoints.Should().BeLessThan(0);
        result.Scorecard.Regime.AdjustedStructureScore.Should().BeLessThan(result.Scorecard.StructureScore!.Value);
        result.Scorecard.Regime.Rationale.Should().Contain("volatility:Panic");
        result.Scorecard.Regime.Rationale.Should().Contain("rates:Inverted");

        // Raw structure score is what gets persisted (formula-version integrity) — not the adjusted one.
        result.Scorecard.Regime.RawStructureScore.Should().Be(result.Scorecard.StructureScore);
    }

    [Fact]
    public async Task Handle_NoRegimeReading_LeavesScoreUnadjusted()
    {
        var structure = new MarketStructureSnapshot(
            "MSFT",
            new Dictionary<int, decimal?> { [21] = 0.1m },
            new Dictionary<int, decimal?> { [21] = 0.1m },
            ExtensionFromMa50: 0.25m,
            TodayZScore: 1.0m,
            VolumeRatio: 2.0m,
            Ma50: 100m,
            Ma200: 90m,
            Stale: false);

        var handler = BuildHandler(
            new FakeCandidateRepository(), new FakeCandidateScoreRepository(), structure: structure, regime: null);
        var result = await handler.Handle(new ScoreCandidateCommand(Guid.NewGuid(), "MSFT"), CancellationToken.None);

        result.Scorecard.Regime.Should().NotBeNull();
        result.Scorecard.Regime!.Rationale.Should().Contain("no_regime_data");
        result.Scorecard.Regime.AdjustedStructureScore.Should().Be(result.Scorecard.StructureScore);
    }
}
