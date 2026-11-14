using System.ComponentModel;
using System.Text.Json;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>#558 stage 2 / rule D1: the correlation fact is part of score_candidate's published output shape.</summary>
public sealed class ScoreCandidateToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static ScoreCandidateResult ResultWith(CorrelationFact? correlation)
        => new(
            Guid.NewGuid(), "CBRS", true,
            new CandidateScorecard(
                70, null, CrowdingClass.Normal, IpsFitFacts.Unknown,
                ScoreEvidence.Empty with { Correlation = correlation }, 1));

    [Fact]
    public async Task ExecuteAsync_ReturnsTheHandlersScorecard_IncludingTheCorrelationFact()
    {
        var fact = new CorrelationFact(
            63,
            [new ReferenceCorrelation("DRAM", 0.6m, 0.56m, 63, null), new ReferenceCorrelation("SOXX", 0.4m, null, 10, CorrelationReasons.InsufficientOverlap)],
            0.56m, 63, 1m, null, 0.5m, true, 20);
        var handler = new Mock<ICommandHandler<ScoreCandidateCommand, ScoreCandidateResult>>();
        handler.Setup(h => h.Handle(It.Is<ScoreCandidateCommand>(c => c.UserId == UserId && c.Ticker == "CBRS"), default))
            .ReturnsAsync(ResultWith(fact));

        var result = await new ScoreCandidateTool(handler.Object, new FakeIdentityResolver { ResolvedUserId = UserId })
            .ExecuteAsync("CBRS");

        result!.Scorecard.Evidence.Correlation.Should().Be(fact);
    }

    [Fact]
    public void OutputShape_PublishesCorrelationFieldsUnderEvidence_WithNullsAndReasonsForNotEvaluable()
    {
        var fact = new CorrelationFact(
            63,
            [new ReferenceCorrelation("DRAM", 0.6m, null, 0, CorrelationReasons.NoBars)],
            null, 12, 0.4m, CorrelationReasons.BookCoverageTooLow, 0.5m, null, 0);

        var json = JsonSerializer.SerializeToElement(ResultWith(fact), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var correlation = json.GetProperty("scorecard").GetProperty("evidence").GetProperty("correlation");
        correlation.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "windowDays", "topHoldings", "bookCorrelation", "bookObservations", "bookCoverage",
            "bookNotEvaluableReason", "flagThreshold", "correlatedToBook", "addPenalty");
        correlation.GetProperty("bookCorrelation").ValueKind.Should().Be(JsonValueKind.Null);
        correlation.GetProperty("correlatedToBook").ValueKind.Should().Be(JsonValueKind.Null);
        correlation.GetProperty("bookNotEvaluableReason").GetString().Should().Be("book_coverage_too_low");
        var top = correlation.GetProperty("topHoldings")[0];
        top.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "symbol", "weight", "correlation", "observations", "notEvaluableReason");
        top.GetProperty("correlation").ValueKind.Should().Be(JsonValueKind.Null);
        top.GetProperty("notEvaluableReason").GetString().Should().Be("no_bars");
    }

    [Fact]
    public void ToolDescription_NamesTheCorrelationFact()
    {
        var description = typeof(ScoreCandidateTool).GetMethod(nameof(ScoreCandidateTool.ExecuteAsync))!
            .GetCustomAttributes(typeof(DescriptionAttribute), false).Cast<DescriptionAttribute>().Single().Description;

        description.Should().Contain("evidence.correlation").And.Contain("correlatedToBook").And.Contain("addPenalty");
    }
}
