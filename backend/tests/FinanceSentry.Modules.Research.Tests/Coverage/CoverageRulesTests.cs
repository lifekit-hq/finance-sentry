namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FluentAssertions;
using Xunit;

public sealed class CoverageRulesTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 14, 1, 10, 0, TimeSpan.Zero);
    private static readonly CoverageOptions Options = new();

    private static DateOnly Day(int daysAgo) => DateOnly.FromDateTime(AsOf.UtcDateTime).AddDays(-daysAgo);

    private static CoverageCandidateFacts Candidate(
        CandidateSource source = CandidateSource.Scan, CandidateStatus status = CandidateStatus.Active, bool note = false)
        => new(Guid.NewGuid(), source, status, AsOf.AddDays(-5), note);

    private static CoverageTickerFacts Facts(
        bool held = false,
        bool watchlisted = false,
        bool thesis = false,
        CoverageCandidateFacts[]? candidates = null,
        CoverageVerdictDay[]? verdicts = null,
        DateTimeOffset? lastEvent = null,
        DateOnly? catalyst = null,
        bool catalystKnown = true,
        int? rankBase = null,
        int penalty = 0)
        => new("ABC", held, watchlisted, thesis, candidates ?? [], verdicts ?? [], lastEvent, catalyst, catalystKnown,
            rankBase, null, penalty);

    private static CoverageVerdictDay V(int daysAgo, string verdict) => new(Day(daysAgo), verdict);

    [Fact]
    public void HeldName_IsAddedPinned_WithTheHeldRule_AheadOfWatchlist()
    {
        var decision = CoverageRules.EvaluateAdd(Facts(held: true, watchlisted: true), Options, AsOf);

        decision.Should().NotBeNull();
        decision!.Rule.Should().Be(CoverageRuleIds.Held);
        decision.Reason.Should().Be(CoverageReason.Held);
        decision.Pinned.Should().BeTrue();
    }

    [Fact]
    public void WatchlistedName_IsAddedPinned()
    {
        var decision = CoverageRules.EvaluateAdd(Facts(watchlisted: true), Options, AsOf);

        decision!.Rule.Should().Be(CoverageRuleIds.Watchlist);
        decision.Reason.Should().Be(CoverageReason.Watchlist);
        decision.Pinned.Should().BeTrue();
    }

    [Theory]
    [InlineData(CandidateSource.Scan, CoverageReason.Screen)]
    [InlineData(CandidateSource.Ledger, CoverageReason.Ledger)]
    public void BuyOnTwoDaysInsideTheWindow_AddsAScanOrLedgerCandidate_AsARankedAdd(CandidateSource source, CoverageReason reason)
    {
        var facts = Facts(candidates: [Candidate(source)], verdicts: [V(10, "Buy"), V(1, "StrongBuy")]);

        var decision = CoverageRules.EvaluateAdd(facts, Options, AsOf);

        decision!.Rule.Should().Be(CoverageRuleIds.BuyTwice);
        decision.Reason.Should().Be(reason);
        decision.Pinned.Should().BeFalse();
    }

    [Fact]
    public void OneBuyDay_ABuyOutsideTheWindow_OrWaitReadings_DoNotAdd()
    {
        var candidate = Candidate();

        CoverageRules.EvaluateAdd(Facts(candidates: [candidate], verdicts: [V(1, "Buy")]), Options, AsOf).Should().BeNull();
        CoverageRules.EvaluateAdd(Facts(candidates: [candidate], verdicts: [V(20, "Buy"), V(1, "Buy")]), Options, AsOf)
            .Should().BeNull("the older Buy fell out of the 14-day window");
        CoverageRules.EvaluateAdd(Facts(candidates: [candidate], verdicts: [V(3, "Wait"), V(1, "Wait")]), Options, AsOf)
            .Should().BeNull();
    }

    [Fact]
    public void TwoReadingsOnTheSameDay_AreOneBuyDay()
    {
        var facts = Facts(candidates: [Candidate()], verdicts: [V(1, "Buy"), V(1, "StrongBuy")]);

        CoverageRules.BuyDaysInWindow(facts, Options, AsOf).Should().Be(1);
        CoverageRules.EvaluateAdd(facts, Options, AsOf).Should().BeNull();
    }

    [Fact]
    public void ABuyHistory_WithoutAnActiveCandidate_DoesNotAdd()
    {
        var facts = Facts(candidates: [Candidate(status: CandidateStatus.Expired)], verdicts: [V(5, "Buy"), V(1, "Buy")]);

        CoverageRules.EvaluateAdd(facts, Options, AsOf).Should().BeNull();
    }

    [Fact]
    public void AUserCandidate_DoesNotQualifyOnVerdicts()
    {
        var facts = Facts(candidates: [Candidate(CandidateSource.User)], verdicts: [V(5, "Buy"), V(1, "Buy")]);

        CoverageRules.EvaluateAdd(facts, Options, AsOf).Should().BeNull();
    }

    [Fact]
    public void LedgerNominationWithADecisionNote_AddsOnTheFirstDay()
    {
        var facts = Facts(candidates: [Candidate(CandidateSource.Ledger, note: true)]);

        var decision = CoverageRules.EvaluateAdd(facts, Options, AsOf);

        decision!.Rule.Should().Be(CoverageRuleIds.LedgerNomination);
        decision.Reason.Should().Be(CoverageReason.Ledger);
    }

    [Fact]
    public void ANominationWithoutANote_OrFromTheScan_IsNotANominationRule()
    {
        CoverageRules.EvaluateAdd(Facts(candidates: [Candidate(CandidateSource.Ledger)]), Options, AsOf).Should().BeNull();
        CoverageRules.EvaluateAdd(Facts(candidates: [Candidate(CandidateSource.Scan, note: true)]), Options, AsOf).Should().BeNull();
    }

    [Fact]
    public void ARankedAdd_IsWithheld_WhenARemoveRuleAlreadyMatchesTheSameFacts()
    {
        // Buy twice, then Avoid twice: the name would be removed the next night on the evidence that added it.
        var facts = Facts(
            candidates: [Candidate()],
            verdicts: [V(6, "Buy"), V(5, "Buy"), V(2, "Avoid"), V(1, "Avoid")]);

        CoverageRules.EvaluateRankedAdd(facts, Options, AsOf).Should().NotBeNull();
        CoverageRules.EvaluateAdd(facts, Options, AsOf).Should().BeNull();
    }

    [Fact]
    public void TwoConsecutiveAvoids_RemoveAnUnprotectedName()
    {
        var facts = Facts(verdicts: [V(3, "Buy"), V(2, "Avoid"), V(1, "Avoid")]);

        var decision = CoverageRules.EvaluateRemove(facts, Options, AsOf, AsOf.AddDays(-30));

        decision!.Rule.Should().Be(CoverageRuleIds.AvoidTwice);
    }

    [Fact]
    public void AnAvoidBrokenByAWait_IsNotConsecutive()
    {
        var facts = Facts(verdicts: [V(3, "Avoid"), V(2, "Wait"), V(1, "Avoid")]);

        CoverageRules.AvoidStreak(facts).Should().Be(1);
        CoverageRules.EvaluateRemove(facts, Options, AsOf, AsOf.AddDays(-30)).Should().BeNull();
    }

    [Fact]
    public void AnExpiredCandidate_RemovesAnUnprotectedName_ButNotWhileAnotherIsActive()
    {
        var expired = Candidate(status: CandidateStatus.Expired);

        CoverageRules.EvaluateRemove(Facts(candidates: [expired]), Options, AsOf, AsOf.AddDays(-30))!
            .Rule.Should().Be(CoverageRuleIds.CandidateExpired);
        CoverageRules.EvaluateRemove(Facts(candidates: [expired, Candidate()]), Options, AsOf, AsOf.AddDays(-30))
            .Should().BeNull();
    }

    [Fact]
    public void SixtyDaysWithoutAnEvent_AndNoCatalyst_RemovesAnUnprotectedName()
    {
        var facts = Facts(lastEvent: AsOf.AddDays(-61));

        CoverageRules.EvaluateRemove(facts, Options, AsOf, AsOf.AddDays(-90))!.Rule.Should().Be(CoverageRuleIds.NoEvent);
    }

    [Fact]
    public void AnIdleClock_StartsAtMembership_WhenNoEventIsKnown()
    {
        CoverageRules.EvaluateRemove(Facts(), Options, AsOf, AsOf.AddDays(-59)).Should().BeNull();
        CoverageRules.EvaluateRemove(Facts(), Options, AsOf, AsOf.AddDays(-60))!.Rule.Should().Be(CoverageRuleIds.NoEvent);
    }

    [Fact]
    public void ADatedCatalystAhead_KeepsAnIdleName_AndPushesRemoveAfterOut()
    {
        var facts = Facts(lastEvent: AsOf.AddDays(-80), catalyst: Day(-10));

        CoverageRules.EvaluateRemove(facts, Options, AsOf, AsOf.AddDays(-90)).Should().BeNull();
        CoverageRules.RemoveAfter(facts, Options, AsOf.AddDays(-90))
            .Should().Be(new DateTimeOffset(Day(-10).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }

    [Fact]
    public void AFailedCatalystLookup_NeverCountsAsNoCatalyst()
    {
        var facts = Facts(lastEvent: AsOf.AddDays(-80), catalystKnown: false);

        CoverageRules.EvaluateRemove(facts, Options, AsOf, AsOf.AddDays(-90)).Should().BeNull();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void HeldWatchlistedOrThesisNames_AreProtected_FromEveryRemoveRule(bool held, bool watchlisted, bool thesis)
    {
        var facts = Facts(
            held, watchlisted, thesis,
            candidates: [Candidate(status: CandidateStatus.Expired)],
            verdicts: [V(2, "Avoid"), V(1, "Avoid")],
            lastEvent: AsOf.AddDays(-200));

        CoverageRules.EvaluateRemove(facts, Options, AsOf, AsOf.AddDays(-300)).Should().BeNull();
        CoverageRules.RemoveAfter(facts, Options, AsOf.AddDays(-300)).Should().BeNull();
    }

    [Fact]
    public void Rank_IsTheScorecardBaseLessTheCorrelationPenalty_AndUnscoredIsZero()
    {
        CoverageRules.Rank(Facts(rankBase: 80, penalty: 20)).Should().Be(60);
        CoverageRules.Rank(Facts(rankBase: 70)).Should().Be(70);
        CoverageRules.Rank(Facts()).Should().Be(0);
    }
}
