namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using System.Text.Json;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>#558 stage 2 / rule D1: score_candidate carries the correlation fact and the configurable add-rule penalty.</summary>
public sealed class CorrelationFactTests
{
    private static readonly IReadOnlyList<BrokerageHoldingSummary> Book =
    [
        new("DRAM", "STK", 1m, 6_000m, DateTime.UtcNow, "ibkr"),
        new("SOXX", "STK", 1m, 3_000m, DateTime.UtcNow, "ibkr"),
        new("XOM", "STK", 1m, 1_000m, DateTime.UtcNow, "ibkr"),
    ];

    private static TickerCorrelation Read(string ticker, decimal? book, string? bookReason = null, decimal coverage = 1m)
        => new(
            ticker,
            63,
            [
                new ReferenceCorrelation("DRAM", 0.6m, 0.56m, 63, null),
                new ReferenceCorrelation("SOXX", 0.3m, null, 12, CorrelationReasons.InsufficientOverlap),
            ],
            new BookCorrelation(book, book is null ? 12 : 63, coverage, bookReason));

    private static async Task<(ScoreCandidateResult Result, FakeCandidateScoreRepository Scores, FakeReturnCorrelationReader Reader)> ScoreAsync(
        TickerCorrelation read, OpportunityOptions? options = null)
    {
        var scores = new FakeCandidateScoreRepository();
        var reader = new FakeReturnCorrelationReader((_, _) => read);
        var handler = new ScoreCandidateCommandHandler(
            new FakeCandidateRepository(), scores, new FakeMarketStructureReader(), new FakeFundamentalsService(),
            new FakeIpsRepository(), new FakePositionCapSource(), new FakeBrokerageHoldingsReader(Book), reader,
            new RecordingRadarSignalWriter(), new FakeMarketRegimeSource(), new RecordingThesisEventRecorder(),
            new FakeOpportunityAlertGenerator(), Options.Create(options ?? new OpportunityOptions()));

        var result = await handler.Handle(new ScoreCandidateCommand(Guid.NewGuid(), "CBRS"), CancellationToken.None);
        return (result, scores, reader);
    }

    [Fact]
    public async Task Scorecard_ExposesTopHoldingAndBookCorrelations_FromTheBooksBars()
    {
        var (result, _, reader) = await ScoreAsync(Read("CBRS", 0.56m));

        var fact = result.Scorecard.Evidence.Correlation!;
        fact.WindowDays.Should().Be(63);
        fact.BookCorrelation.Should().Be(0.56m);
        fact.BookObservations.Should().Be(63);
        fact.BookCoverage.Should().Be(1m);
        fact.TopHoldings.Select(h => (h.Symbol, h.Correlation)).Should().Equal(("DRAM", 0.56m), ("SOXX", null));
        fact.TopHoldings[1].NotEvaluableReason.Should().Be(CorrelationReasons.InsufficientOverlap);

        var call = reader.Calls.Should().ContainSingle().Subject;
        call.Ticker.Should().Be("CBRS");
        call.Book.Select(r => (r.Symbol, r.Weight)).Should().Equal(("DRAM", 6_000m), ("SOXX", 3_000m), ("XOM", 1_000m));
        call.TopHoldings.Should().Be(5);
        call.MinBookCoverage.Should().Be(0.5m);
    }

    [Fact]
    public async Task Flag_AtOrAboveThreshold_SetsCorrelatedToBook_AndTheConfiguredPenalty()
    {
        var (result, _, _) = await ScoreAsync(Read("CBRS", 0.56m));

        var fact = result.Scorecard.Evidence.Correlation!;
        fact.CorrelatedToBook.Should().BeTrue();
        fact.FlagThreshold.Should().Be(0.5m);
        fact.AddPenalty.Should().Be(20);
    }

    [Fact]
    public async Task Flag_ExactlyAtThreshold_IsFlagged()
    {
        var (result, _, _) = await ScoreAsync(Read("CBRS", 0.5m));

        result.Scorecard.Evidence.Correlation!.CorrelatedToBook.Should().BeTrue();
    }

    [Fact]
    public async Task Flag_BelowThreshold_IsClear_WithZeroPenalty_AndNegativeCorrelationNeverFlags()
    {
        var (low, _, _) = await ScoreAsync(Read("XOM2", 0.12m));
        var (negative, _, _) = await ScoreAsync(Read("GLD", -0.7m));

        low.Scorecard.Evidence.Correlation!.CorrelatedToBook.Should().BeFalse();
        low.Scorecard.Evidence.Correlation!.AddPenalty.Should().Be(0);
        negative.Scorecard.Evidence.Correlation!.CorrelatedToBook.Should().BeFalse();
        negative.Scorecard.Evidence.Correlation!.AddPenalty.Should().Be(0);
    }

    [Fact]
    public async Task NotEvaluable_BookFigureNull_KeepsReason_NoFlag_NoPenalty()
    {
        var (result, _, _) = await ScoreAsync(Read("NEWCO", null, CorrelationReasons.InsufficientOverlap));

        var fact = result.Scorecard.Evidence.Correlation!;
        fact.BookCorrelation.Should().BeNull();
        fact.BookNotEvaluableReason.Should().Be(CorrelationReasons.InsufficientOverlap);
        fact.CorrelatedToBook.Should().BeNull();
        fact.AddPenalty.Should().Be(0);
    }

    [Fact]
    public async Task Thresholds_AreConfigurable()
    {
        var options = new OpportunityOptions
        {
            CorrelationToBookFlagThreshold = 0.7m, CorrelationToBookPenalty = 35, CorrelationTopHoldings = 3, CorrelationMinBookCoverage = 0.8m,
        };

        var (stillClear, _, _) = await ScoreAsync(Read("CBRS", 0.56m), options);
        var (flagged, _, reader) = await ScoreAsync(Read("CBRS", 0.75m), options);

        stillClear.Scorecard.Evidence.Correlation!.CorrelatedToBook.Should().BeFalse();
        flagged.Scorecard.Evidence.Correlation!.AddPenalty.Should().Be(35);
        reader.Calls.Single().TopHoldings.Should().Be(3);
        reader.Calls.Single().MinBookCoverage.Should().Be(0.8m);
    }

    [Fact]
    public async Task Flag_DoesNotChangeTheSubScores_OrCrowding()
    {
        var (flagged, _, _) = await ScoreAsync(Read("CBRS", 0.9m));
        var (clear, _, _) = await ScoreAsync(Read("CBRS", 0.0m));

        flagged.Scorecard.StructureScore.Should().Be(clear.Scorecard.StructureScore);
        flagged.Scorecard.FundamentalsScore.Should().Be(clear.Scorecard.FundamentalsScore);
        flagged.Scorecard.Crowding.Should().Be(clear.Scorecard.Crowding);
        flagged.Scorecard.Regime.Should().BeEquivalentTo(clear.Scorecard.Regime);
    }

    [Fact]
    public async Task Fact_IsPersistedInTheScoreEvidence_AndSurvivesTheJsonColumnRoundTrip()
    {
        var (result, scores, _) = await ScoreAsync(Read("CBRS", 0.56m));

        var persisted = scores.Scores.Should().ContainSingle().Subject.Evidence;
        persisted.Correlation.Should().BeSameAs(result.Scorecard.Evidence.Correlation);

        var json = JsonSerializer.Serialize(persisted);
        var back = JsonSerializer.Deserialize<ScoreEvidence>(json)!;
        back.Correlation!.BookCorrelation.Should().Be(0.56m);
        back.Correlation.CorrelatedToBook.Should().BeTrue();
        back.Correlation.TopHoldings.Should().HaveCount(2);
    }

    [Fact]
    public void ScoreEvidence_StoredBeforeThisFact_DeserialisesWithNullCorrelation_SoNoMigrationIsNeeded()
    {
        var legacy = JsonSerializer.Serialize(ScoreEvidence.Empty);
        var node = System.Text.Json.Nodes.JsonNode.Parse(legacy)!.AsObject();
        node.Remove(nameof(ScoreEvidence.Correlation));

        var back = JsonSerializer.Deserialize<ScoreEvidence>(node.ToJsonString())!;

        back.Correlation.Should().BeNull();
    }
}
