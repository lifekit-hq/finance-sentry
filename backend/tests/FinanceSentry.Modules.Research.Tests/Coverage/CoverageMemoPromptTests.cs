namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FluentAssertions;
using Xunit;

public sealed class CoverageMemoPromptTests
{
    [Fact]
    public void System_NamesTheClosedMetricVocabulary_AndThePaperOnlyStance()
    {
        CoverageMemoPrompt.System.Should().Contain("revenue_yoy").And.Contain("price_drawdown").And.Contain("nothing you write places");
    }

    [Fact]
    public void User_CarriesTheEntryTheScorecardAndTheEvents_WithoutTheRuleTable()
    {
        var text = CoverageMemoPrompt.User(Evidence(), 100_000);

        text.Should().Contain("MU").And.Contain("buy_twice_in_window").And.Contain("10-Q filed").And.Contain("\"word\":\"Buy\"");
        text.Should().NotContain("\"rules\":[{");
    }

    [Fact]
    public void User_CutsTheEvidenceToTheCharacterBudget()
    {
        var text = CoverageMemoPrompt.User(Evidence(), 300);

        text.Should().StartWith("Evidence (JSON):");
        text.Should().Contain("evidence cut here");
        text.Length.Should().BeLessThan(400);
    }

    private static CoverageMemoEvidence Evidence()
    {
        var verdict = VerdictRules.Evaluate(
            new VerdictInputs(80, 72, CrowdingClass.Normal, false, 15m), new VerdictOptions());
        var card = new ScorecardResult(
            "MU", DateTimeOffset.UtcNow, 1,
            new ScorecardStructureAxis(80, 80, 0, [], null, null, null, new Dictionary<int, decimal?>(), new Dictionary<int, decimal?>(), null, null, null, null, null, null, []),
            new ScorecardFundamentalsAxis(72, null, null, null, null, null, []),
            new ScorecardCrowdingAxis("Normal", null, null, 0m, 0m, 0m, null),
            ScorecardValuationAxis.Unavailable,
            VerdictDto.From(verdict, VerdictDto.AdjustedBasis));
        var entry = new CoverageJournalEntry
        {
            Ticker = "MU", Action = CoverageAction.Added, Rule = CoverageRuleIds.BuyTwice, Reason = CoverageReason.Screen,
            Detail = "Scored Buy on 2 days.", RankScore = 71,
        };
        var coverageEvent = new CoverageEvent { Ticker = "MU", Source = "filing", Summary = "10-Q filed 2026-11-12" };
        return new CoverageMemoEvidence(entry, null, card, [coverageEvent]);
    }
}
