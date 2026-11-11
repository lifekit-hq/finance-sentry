namespace FinanceSentry.Modules.Research.Tests.Scorecards;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>The batch read behind the Accounts chips: one call, the single read's word per symbol, nothing else.</summary>
public sealed class GetScorecardVerdictsQueryTests
{
    [Fact]
    public async Task Handle_ReadsEachDistinctSymbolOnceThroughTheSingleScorecardRead()
    {
        var single = SingleRead(("AAA", 78), ("BBB", 9));

        var result = await HandlerFor(single).Handle(
            new GetScorecardVerdictsQuery([" aaa ", "BBB", "AAA", ""]), CancellationToken.None);

        result.Select(r => (r.Symbol, r.Word)).Should().Equal(("AAA", "Buy"), ("BBB", "Avoid"));
        result[1].FiredRuleId.Should().Be(VerdictRules.WeakStructure);
        single.Verify(h => h.Handle(It.IsAny<GetScorecardQuery>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_LeavesOutASymbolThatCannotBeRead_AndKeepsTheRest()
    {
        var single = SingleRead(("AAA", 78));
        single.Setup(h => h.Handle(It.Is<GetScorecardQuery>(q => q.Symbol == "BAD"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("source down"));

        var result = await HandlerFor(single).Handle(new GetScorecardVerdictsQuery(["BAD", "AAA"]), CancellationToken.None);

        result.Select(r => r.Symbol).Should().Equal("AAA");
    }

    [Fact]
    public async Task Handle_CapsTheBatch()
    {
        var single = new Mock<IQueryHandler<GetScorecardQuery, ScorecardResult>>();
        single.Setup(h => h.Handle(It.IsAny<GetScorecardQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetScorecardQuery q, CancellationToken _) => Card(q.Symbol, 78));
        var symbols = Enumerable.Range(0, GetScorecardVerdictsQuery.MaxSymbols + 20).Select(i => $"S{i}").ToList();

        var result = await HandlerFor(single).Handle(new GetScorecardVerdictsQuery(symbols), CancellationToken.None);

        result.Should().HaveCount(GetScorecardVerdictsQuery.MaxSymbols);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheWordAndWhatTurnsItBuy_NotTheReasonsOrTheRuleTable()
    {
        var result = await HandlerFor(SingleRead(("AAA", 78))).Handle(
            new GetScorecardVerdictsQuery(["AAA"]), CancellationToken.None);

        typeof(ScorecardVerdictSummary).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("Symbol", "Word", "FiredRuleId", "TurnsBuyWhen");
        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_CarriesTheSingleReadsTurnsBuyLine_AndNothingWhereThereIsNoBarToName()
    {
        var single = SingleRead(("AAA", 78), ("BBB", 9));

        var result = await HandlerFor(single).Handle(new GetScorecardVerdictsQuery(["AAA", "BBB"]), CancellationToken.None);

        result[0].TurnsBuyWhen.Should().BeNull("a BUY has no bar left to clear");
        result[1].TurnsBuyWhen.Should().Be(Card("BBB", 9).Verdict.TurnsBuyWhen).And.NotBeNullOrWhiteSpace();
    }

    private static GetScorecardVerdictsQueryHandler HandlerFor(Mock<IQueryHandler<GetScorecardQuery, ScorecardResult>> single) =>
        new(single.Object, NullLogger<GetScorecardVerdictsQueryHandler>.Instance);

    private static Mock<IQueryHandler<GetScorecardQuery, ScorecardResult>> SingleRead(params (string Symbol, int Structure)[] cards)
    {
        var single = new Mock<IQueryHandler<GetScorecardQuery, ScorecardResult>>();
        foreach (var (symbol, structure) in cards)
        {
            single.Setup(h => h.Handle(It.Is<GetScorecardQuery>(q => q.Symbol == symbol), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Card(symbol, structure));
        }

        return single;
    }

    private static ScorecardResult Card(string symbol, int structure)
    {
        var verdict = VerdictRules.Evaluate(
            new VerdictInputs(structure, 72, CrowdingClass.Normal, false, 15m), new VerdictOptions());
        return new ScorecardResult(
            symbol,
            DateTimeOffset.UtcNow,
            1,
            new ScorecardStructureAxis(structure, structure, 0, [], null, null, null, new Dictionary<int, decimal?>(), new Dictionary<int, decimal?>(), null, null, null, null, null, null, []),
            new ScorecardFundamentalsAxis(72, null, null, null, null, null, []),
            new ScorecardCrowdingAxis("Normal", null, null, 0m, 0m, 0m, null),
            ScorecardValuationAxis.Unavailable,
            VerdictDto.From(verdict, VerdictDto.AdjustedBasis));
    }
}
