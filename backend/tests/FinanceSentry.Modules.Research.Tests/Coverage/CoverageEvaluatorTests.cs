namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

public sealed class CoverageEvaluatorTests
{
    private static readonly DateTimeOffset Day1 = CoverageHarness.Day1;

    [Fact]
    public async Task FirstRun_SeedsFromHoldingsAndWatchlist_AndJournalsEachAddWithItsRule()
    {
        var h = new CoverageHarness();
        h.Hold("NVDA");
        h.Hold("DRAM", type: "STK");
        h.Hold("BTC", type: "CRYPTO");
        h.WatchedTickers.AddRange(["AVGO", "NVDA"]);

        var summary = await h.RunAsync(Day1);

        summary.Added.Should().Be(3);
        (await h.MembersAsync()).Select(m => (m.Ticker, m.AddedReason)).Should().Equal(
            ("AVGO", CoverageReason.Watchlist), ("DRAM", CoverageReason.Held), ("NVDA", CoverageReason.Held));
        (await h.JournalAsync()).Select(e => (e.Ticker, e.Action, e.Rule)).Should().BeEquivalentTo(
        [
            ("AVGO", CoverageAction.Added, CoverageRuleIds.Watchlist),
            ("DRAM", CoverageAction.Added, CoverageRuleIds.Held),
            ("NVDA", CoverageAction.Added, CoverageRuleIds.Held),
        ]);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing_AndWritesNoJournal()
    {
        var h = new CoverageHarness();
        h.Hold("NVDA");
        await h.RunAsync(Day1);

        var again = await h.RunAsync(Day1.AddDays(1));

        again.Added.Should().Be(0);
        again.Removed.Should().Be(0);
        (await h.JournalAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task ScanCandidate_ReadingBuyOnTwoDays_JoinsOnTheSecondDay_WithScreenReasonAndRank()
    {
        var h = new CoverageHarness();
        await h.CandidateAsync("MU", structure: 80, fundamentals: 60);
        h.Verdicts.Verdicts["MU"] = "Buy";

        (await h.RunAsync(Day1)).Added.Should().Be(0);
        h.Verdicts.Reads.Should().Equal("MU");

        var second = await h.RunAsync(Day1.AddDays(1));

        second.Added.Should().Be(1);
        var member = (await h.MembersAsync()).Single();
        member.Ticker.Should().Be("MU");
        member.AddedReason.Should().Be(CoverageReason.Screen);
        member.LastScoreId.Should().NotBeNull();
        var entry = (await h.JournalAsync()).Single();
        entry.Rule.Should().Be(CoverageRuleIds.BuyTwice);
        entry.RankScore.Should().Be(70);
        entry.Detail.Should().Contain("2 days");
    }

    [Fact]
    public async Task LedgerCandidate_ReadingStrongBuyTwice_JoinsWithLedgerReason()
    {
        var h = new CoverageHarness();
        await h.CandidateAsync("VRT", CandidateSource.Ledger);
        h.Verdicts.Verdicts["VRT"] = "StrongBuy";

        await h.RunAsync(Day1);
        await h.RunAsync(Day1.AddDays(1));

        (await h.MembersAsync()).Single().AddedReason.Should().Be(CoverageReason.Ledger);
    }

    [Fact]
    public async Task OneReadingPerDay_ARerunTheSameDayDoesNotReadOrDoubleCount()
    {
        var h = new CoverageHarness();
        await h.CandidateAsync("MU");
        h.Verdicts.Verdicts["MU"] = "Buy";

        await h.RunAsync(Day1);
        await h.RunAsync(Day1.AddHours(2));

        h.Verdicts.Reads.Should().HaveCount(1);
        (await h.Db.CoverageVerdictObservations.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await h.MembersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AScorecardThatCannotBeRead_RecordsNoReading()
    {
        var h = new CoverageHarness();
        await h.CandidateAsync("MU");
        h.Verdicts.Verdicts["MU"] = null;

        await h.RunAsync(Day1);

        (await h.Db.CoverageVerdictObservations.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ACandidateNotScoredInsideTheWindow_IsNotRead()
    {
        var h = new CoverageHarness();
        await h.CandidateAsync("MU", scoredAt: Day1.AddDays(-20));
        h.Verdicts.Verdicts["MU"] = "Buy";

        await h.RunAsync(Day1);

        h.Verdicts.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task LedgerNominationWithADecisionNote_JoinsAtOnce_WithoutAVerdictHistory()
    {
        var h = new CoverageHarness();
        await h.CandidateAsync("OKLO", CandidateSource.Ledger, decisionNote: "Fission power for AI sites; early.");
        await h.CandidateAsync("SMR", CandidateSource.Ledger);
        h.Verdicts.Verdicts["OKLO"] = "Wait";
        h.Verdicts.Verdicts["SMR"] = "Wait";

        await h.RunAsync(Day1);

        (await h.MembersAsync()).Select(m => m.Ticker).Should().Equal("OKLO");
        (await h.JournalAsync()).Single().Rule.Should().Be(CoverageRuleIds.LedgerNomination);
    }

    [Fact]
    public async Task TwoConsecutiveAvoids_RemoveANonHeldMember_AndJournalTheRule()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", lastEvent: Day1);
        await h.CandidateAsync("MU");
        h.Verdicts.Verdicts["MU"] = "Avoid";

        await h.RunAsync(Day1);
        (await h.TickersAsync()).Should().Equal("MU");
        var second = await h.RunAsync(Day1.AddDays(1));

        second.Removed.Should().Be(1);
        (await h.MembersAsync()).Should().BeEmpty();
        var entry = (await h.JournalAsync()).Single();
        (entry.Action, entry.Rule, entry.Reason).Should().Be((CoverageAction.Removed, CoverageRuleIds.AvoidTwice, CoverageReason.Screen));
    }

    [Fact]
    public async Task AHeldMember_IsNeverRemoved_EvenOnTwoAvoids()
    {
        var h = new CoverageHarness();
        h.Hold("MU");
        await h.CandidateAsync("MU");
        h.Verdicts.Verdicts["MU"] = "Avoid";

        await h.RunAsync(Day1);
        await h.RunAsync(Day1.AddDays(1));
        await h.RunAsync(Day1.AddDays(2));

        (await h.TickersAsync()).Should().Equal("MU");
        (await h.JournalAsync()).Should().OnlyContain(e => e.Action == CoverageAction.Added);
    }

    [Fact]
    public async Task AnExpiredCandidate_RemovesANonHeldMember()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", lastEvent: Day1);
        await h.CandidateAsync("MU", status: CandidateStatus.Expired);

        await h.RunAsync(Day1);

        (await h.MembersAsync()).Should().BeEmpty();
        (await h.JournalAsync()).Single().Rule.Should().Be(CoverageRuleIds.CandidateExpired);
    }

    [Fact]
    public async Task SixtyIdleDays_WithNoCatalyst_RemoveTheMember()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", addedAt: Day1.AddDays(-70), lastEvent: Day1.AddDays(-61));

        await h.RunAsync(Day1);

        (await h.MembersAsync()).Should().BeEmpty();
        var entry = (await h.JournalAsync()).Single();
        entry.Rule.Should().Be(CoverageRuleIds.NoEvent);
        entry.Detail.Should().Contain("No event since");
    }

    [Fact]
    public async Task AnUpcomingEarningsDate_KeepsAnIdleMember_AndShowsWhenItWouldGo()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", addedAt: Day1.AddDays(-70), lastEvent: Day1.AddDays(-61));
        h.EarningsDates["MU"] = DateOnly.FromDateTime(Day1.UtcDateTime).AddDays(12);

        await h.RunAsync(Day1);

        var member = (await h.MembersAsync()).Single();
        member.RemoveAfter.Should().Be(new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero));
        (await h.JournalAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedEarningsLookup_KeepsAnIdleMember_RatherThanRemovingItOnAMissingAnswer()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", addedAt: Day1.AddDays(-70), lastEvent: Day1.AddDays(-61));
        h.Earnings
            .Setup(e => e.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));

        await h.RunAsync(Day1);

        (await h.TickersAsync()).Should().Equal("MU");
    }

    [Fact]
    public async Task ThesisCatalystAhead_KeepsAnIdleMember_AndAnActiveThesisProtectsIt()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", addedAt: Day1.AddDays(-70), lastEvent: Day1.AddDays(-61));
        await h.MemberAsync("AMD", addedAt: Day1.AddDays(-70), lastEvent: Day1.AddDays(-61));
        await h.ThesisAsync("MU", catalyst: DateOnly.FromDateTime(Day1.UtcDateTime).AddDays(20));
        await h.ThesisAsync("AMD");

        await h.RunAsync(Day1);

        (await h.TickersAsync()).Should().Equal("AMD", "MU");
        (await h.MembersAsync()).Single(m => m.Ticker == "AMD").RemoveAfter.Should().BeNull("a thesis protects the name");
    }

    [Fact]
    public async Task ABrokenThesis_NoLongerProtects()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", addedAt: Day1.AddDays(-70), lastEvent: Day1.AddDays(-61));
        await h.ThesisAsync("MU", broken: true);

        await h.RunAsync(Day1);

        (await h.MembersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AWatchlistedMember_IsNotRemovedWhileOnTheWatchlist_AndIsRemovedOnceOff()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", CoverageReason.Watchlist, addedAt: Day1.AddDays(-70), lastEvent: Day1.AddDays(-61));
        h.WatchedTickers.Add("MU");
        await h.RunAsync(Day1);
        (await h.TickersAsync()).Should().Equal("MU");

        h.WatchedTickers.Clear();
        await h.RunAsync(Day1.AddDays(1));

        (await h.MembersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheCapBinds_ARankedAddComesInByScorecardLessTheCorrelationPenalty()
    {
        var h = new CoverageHarness(new Application.Services.Coverage.CoverageOptions { MaxMembers = 2 });
        h.Hold("DRAM");
        // Both qualify by a Ledger nomination note; one slot is free. HOT scores higher but moves with the book.
        await h.CandidateAsync("HOT", CandidateSource.Ledger, structure: 85, fundamentals: 75, decisionNote: "n");
        await h.CandidateAsync("COOL", CandidateSource.Ledger, structure: 70, fundamentals: 70, decisionNote: "n");
        h.Correlation.Book["HOT"] = 0.62m;
        h.Correlation.Book["COOL"] = 0.10m;

        var summary = await h.RunAsync(Day1);

        summary.Deferred.Should().Be(1);
        (await h.TickersAsync()).Should().Equal("COOL", "DRAM");
        var entry = (await h.JournalAsync()).Single(e => e.Ticker == "COOL");
        entry.RankScore.Should().Be(70);
        entry.CorrelationToBook.Should().Be(0.10m);
    }

    [Fact]
    public async Task WithoutTheCorrelationPenalty_TheHigherScoreWinsTheSlot()
    {
        var h = new CoverageHarness(new Application.Services.Coverage.CoverageOptions { MaxMembers = 1 });
        await h.CandidateAsync("HOT", CandidateSource.Ledger, structure: 85, fundamentals: 75, decisionNote: "n");
        await h.CandidateAsync("COOL", CandidateSource.Ledger, structure: 70, fundamentals: 70, decisionNote: "n");
        h.Correlation.Book["HOT"] = 0.20m;
        h.Correlation.Book["COOL"] = 0.10m;

        await h.RunAsync(Day1);

        (await h.TickersAsync()).Should().Equal("HOT");
    }

    [Fact]
    public async Task ACorrelationThatIsNotEvaluable_CarriesNoPenaltyAndIsStoredAsNull()
    {
        var h = new CoverageHarness(new Application.Services.Coverage.CoverageOptions { MaxMembers = 1 });
        await h.CandidateAsync("NOBARS", CandidateSource.Ledger, structure: 90, fundamentals: 90, decisionNote: "n");
        await h.CandidateAsync("OTHER", CandidateSource.Ledger, structure: 80, fundamentals: 80, decisionNote: "n");
        h.Correlation.Book["OTHER"] = 0.9m;

        await h.RunAsync(Day1);

        var member = (await h.MembersAsync()).Single();
        member.Ticker.Should().Be("NOBARS");
        member.CorrelationToBook.Should().BeNull();
    }

    [Fact]
    public async Task HeldAndWatchlistedNames_AlwaysJoin_EvenOverTheCap_AndOnlyRankedAddsAreCapped()
    {
        var h = new CoverageHarness(new Application.Services.Coverage.CoverageOptions { MaxMembers = 1 });
        h.Hold("AAA");
        h.Hold("BBB");
        await h.CandidateAsync("CCC", CandidateSource.Ledger, decisionNote: "n");

        var summary = await h.RunAsync(Day1);

        (await h.TickersAsync()).Should().Equal("AAA", "BBB");
        summary.Deferred.Should().Be(1);
        (await h.JournalAsync()).Should().OnlyContain(e => e.Detail.Contains("Over the cap of 1"));
    }

    [Fact]
    public async Task ADeferredName_IsNotJournaled_AndJoinsWhenASlotFrees()
    {
        var h = new CoverageHarness(new Application.Services.Coverage.CoverageOptions { MaxMembers = 1 });
        h.Hold("AAA");
        await h.CandidateAsync("CCC", CandidateSource.Ledger, decisionNote: "n");
        await h.RunAsync(Day1);
        (await h.TickersAsync()).Should().Equal("AAA");

        h.Holdings.Clear();
        await h.RunAsync(Day1.AddDays(1));
        // AAA is no longer held: it leaves by the idle rule only after 60 days, so the slot stays taken.
        (await h.TickersAsync()).Should().Equal("AAA");

        await h.RunAsync(Day1.AddDays(70));

        (await h.TickersAsync()).Should().Equal("CCC");
        (await h.JournalAsync()).Select(e => (e.Ticker, e.Action)).Should().Equal(
            ("AAA", CoverageAction.Added), ("AAA", CoverageAction.Removed), ("CCC", CoverageAction.Added));
    }

    [Fact]
    public async Task ARankedAdd_IsNotMadeWhenItsOwnHistoryWouldRemoveItAtOnce()
    {
        var h = new CoverageHarness();
        await h.CandidateAsync("MU");
        var today = DateOnly.FromDateTime(Day1.UtcDateTime);
        foreach (var (days, verdict) in new[] { (6, "Buy"), (5, "Buy"), (2, "Avoid") })
        {
            h.Db.CoverageVerdictObservations.Add(new CoverageVerdictObservation
            {
                UserId = CoverageHarness.UserId, Ticker = "MU", ObservedOn = today.AddDays(-days), Verdict = verdict, FiredRuleId = "x",
            });
        }

        await h.Db.SaveChangesAsync();
        h.Verdicts.Verdicts["MU"] = "Avoid";

        await h.RunAsync(Day1);

        (await h.MembersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AMember_RefreshesItsLastEvent_LastScoreAndCorrelation()
    {
        var h = new CoverageHarness();
        h.Hold("DRAM", 5000m);
        await h.MemberAsync("MU", lastEvent: Day1.AddDays(-30));
        await h.CandidateAsync("MU", scoredAt: Day1.AddDays(-2));
        h.Correlation.Book["MU"] = 0.44m;

        await h.RunAsync(Day1);

        var member = (await h.MembersAsync()).Single(m => m.Ticker == "MU");
        member.LastEventAt.Should().Be(Day1.AddDays(-2));
        member.LastScoreId.Should().NotBeNull();
        member.CorrelationToBook.Should().Be(0.44m);
        member.RemoveAfter.Should().Be(Day1.AddDays(-2).AddDays(60));
    }

    [Fact]
    public async Task ASnapshotEvent_IsNotAnEvent()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", addedAt: Day1.AddDays(-70));
        h.Db.ThesisEvents.Add(new Domain.ThesisEvent
        {
            UserId = CoverageHarness.UserId, SubjectType = Domain.ThesisSubjectType.Thesis, SubjectId = Guid.NewGuid(),
            Ticker = "MU", EventType = Domain.ThesisEventType.Snapshot, Timestamp = Day1.AddDays(-1),
        });
        await h.Db.SaveChangesAsync();

        await h.RunAsync(Day1);

        (await h.MembersAsync()).Should().BeEmpty("the weekly price snapshot does not keep an idle name covered");
    }

    [Fact]
    public async Task OneUserFailing_DoesNotStopTheOthers_ButFailsTheRun()
    {
        var h = new CoverageHarness();
        var other = Guid.NewGuid();
        h.Users.Add(other);
        h.Hold("NVDA");
        h.Brokerage.Setup(b => b.GetHoldingsAsync(other, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sync down"));

        var act = () => h.Evaluator.EvaluateAllAsync(Day1);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*1 of 2*");
        (await h.TickersAsync()).Should().Equal("NVDA");
    }

    [Fact]
    public async Task OldReadings_ArePrunedByTheRun()
    {
        var h = new CoverageHarness();
        h.Db.CoverageVerdictObservations.Add(new CoverageVerdictObservation
        {
            UserId = CoverageHarness.UserId, Ticker = "OLD", ObservedOn = DateOnly.FromDateTime(Day1.UtcDateTime).AddDays(-200),
            Verdict = "Buy", FiredRuleId = "x",
        });
        await h.Db.SaveChangesAsync();

        await h.Evaluator.EvaluateAllAsync(Day1);

        (await h.Db.CoverageVerdictObservations.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ARemovedName_CanComeBack_WithANewJournalRow()
    {
        var h = new CoverageHarness();
        await h.MemberAsync("MU", lastEvent: Day1);
        await h.CandidateAsync("MU", status: CandidateStatus.Expired);
        await h.RunAsync(Day1);
        (await h.MembersAsync()).Should().BeEmpty();

        h.WatchedTickers.Add("MU");
        await h.RunAsync(Day1.AddDays(1));

        (await h.MembersAsync()).Single().AddedReason.Should().Be(CoverageReason.Watchlist);
        (await h.JournalAsync()).Select(e => e.Action).Should().Equal(CoverageAction.Removed, CoverageAction.Added);
    }
}
