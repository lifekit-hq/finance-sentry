namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FluentAssertions;
using Moq;
using Xunit;

public sealed class CoverageExplainerTests
{
    private static readonly DateTimeOffset Day1 = CoverageHarness.Day1;

    private static Task<CoverageExplanation> ExplainAsync(CoverageHarness h, string ticker)
        => h.Explainer.ExplainAsync(CoverageHarness.UserId, ticker, Day1);

    [Fact]
    public async Task AHeldMember_ExplainsItsAddRule_AndThatNoRemoveRuleCanActOnIt()
    {
        var h = new CoverageHarness();
        h.Hold("NVDA");
        await h.RunAsync(Day1.AddDays(-1));

        var explanation = await ExplainAsync(h, "nvda");

        explanation.IsMember.Should().BeTrue();
        explanation.Ticker.Should().Be("NVDA");
        explanation.Summary.Should().Contain("Covered since 2026-11-13 (Held)").And.Contain("cannot be removed");
        explanation.Member!.AddedRule.Should().Be(CoverageRuleIds.Held);
        explanation.Member.Held.Should().BeTrue();
        explanation.AddRules.Single(r => r.Rule == CoverageRuleIds.Held).Met.Should().BeTrue();
        explanation.RemoveRules.Should().OnlyContain(r => !r.Applies && !r.Met || !r.Applies);
        explanation.RemoveRules.Select(r => r.Rule).Should().Equal(
            CoverageRuleIds.AvoidTwice, CoverageRuleIds.CandidateExpired, CoverageRuleIds.NoEvent);
        explanation.Journal.Should().ContainSingle(e => e.Action == "Added" && e.Rule == CoverageRuleIds.Held);
    }

    [Fact]
    public async Task ANonMemberWithALedgerNominationNote_SaysItIsAddedNextRun_AndShowsItsRank()
    {
        var h = new CoverageHarness();
        await h.CandidateAsync("OKLO", CandidateSource.Ledger, structure: 60, fundamentals: 80, decisionNote: "early");
        h.Correlation.Book["OKLO"] = 0.7m;

        var explanation = await ExplainAsync(h, "OKLO");

        explanation.IsMember.Should().BeFalse();
        explanation.Summary.Should().StartWith("Not yet covered: ledger_nomination is met");
        explanation.AddRules.Single(r => r.Rule == CoverageRuleIds.LedgerNomination).Met.Should().BeTrue();
        explanation.RankBase.Should().Be(70);
        explanation.CorrelationPenalty.Should().Be(20);
        explanation.Rank.Should().Be(50);
    }

    [Fact]
    public async Task ANonMemberThatMeetsNoRule_SaysSo_AndWhatItWouldTake()
    {
        var h = new CoverageHarness();
        await h.CandidateAsync("MU");

        var explanation = await ExplainAsync(h, "MU");

        explanation.Summary.Should().StartWith("Not covered: no add rule is met");
        explanation.AddRules.Single(r => r.Rule == CoverageRuleIds.BuyTwice).Detail.Should().Contain("0 of 2");
        explanation.AddRules.Single(r => r.Rule == CoverageRuleIds.LedgerNomination).Detail.Should().Contain("not a Ledger nomination");
    }

    [Fact]
    public async Task WhenTheCapIsFull_AQualifyingNonMemberIsToldItWaitsForASlot()
    {
        var h = new CoverageHarness(new CoverageOptions { MaxMembers = 1 });
        h.Hold("AAA");
        await h.RunAsync(Day1.AddDays(-1));
        await h.CandidateAsync("CCC", CandidateSource.Ledger, decisionNote: "n");

        var explanation = await ExplainAsync(h, "CCC");

        explanation.Cap.Should().Be(new CoverageCapView(1, 1, true));
        explanation.Summary.Should().Contain("the cap of 1 is full");
    }

    [Fact]
    public async Task AnIdleUnprotectedMember_ReportsWhenTheNoEventRuleWouldRemoveIt()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", addedAt: Day1.AddDays(-20), lastEvent: Day1.AddDays(-10));

        var explanation = await ExplainAsync(h, "MU");

        explanation.Summary.Should().Contain("Covered since").And.Contain("is removed after");
        var rule = explanation.RemoveRules.Single(r => r.Rule == CoverageRuleIds.NoEvent);
        rule.Applies.Should().BeTrue();
        rule.Met.Should().BeFalse();
        rule.Detail.Should().Contain("Last event 2026-11-04").And.Contain("next dated catalyst none");
    }

    [Fact]
    public async Task AMemberCarryingAThesis_IsProtected_AndSaysSo()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", addedAt: Day1.AddDays(-100));
        await h.ThesisAsync("MU");

        var explanation = await ExplainAsync(h, "MU");

        explanation.HasThesis.Should().BeTrue();
        explanation.Member!.HasThesis.Should().BeTrue();
        explanation.RemoveRules.Should().OnlyContain(r => !r.Applies && r.Detail.Contains("carrying an active thesis"));
    }

    [Fact]
    public async Task AFailedEarningsLookup_IsReportedAsUnknown_NotAsNone()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", addedAt: Day1.AddDays(-100));
        h.Earnings.Reset();
        h.Earnings
            .Setup(e => e.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("down"));

        var explanation = await ExplainAsync(h, "MU");

        explanation.RemoveRules.Single(r => r.Rule == CoverageRuleIds.NoEvent).Detail.Should().Contain("unknown");
        explanation.RemoveRules.Single(r => r.Rule == CoverageRuleIds.NoEvent).Met.Should().BeFalse();
    }
}
