namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>A clock the test moves.</summary>
internal sealed class MovableClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Events on coverage names (discovery funnel stage 4) end to end over a real in-memory store: the filing detector, the
/// review-due raiser, the coverage event reviewer, the evaluator's remove rules and the digest. Only the outside world
/// (EDGAR, the alert generator, the candidate scorer, the verdict source) is scripted.
/// </summary>
public sealed class CoverageEventsTests
{
    private static readonly DateTime MondayUtc = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 16);

    private readonly CoverageHarness _h = new();
    private readonly MovableClock _clock = new(new DateTimeOffset(MondayUtc));
    private readonly Mock<ISecEdgarService> _edgar = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly Mock<ICryptoHoldingsReader> _crypto = new();
    private readonly Mock<ICommandHandler<ScoreCandidateCommand, ScoreCandidateResult>> _scorer = new();
    private readonly List<ReviewDue> _reviews = [];
    private readonly List<string> _rescored = [];

    public CoverageEventsTests()
    {
        _crypto.Setup(c => c.GetHoldingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _edgar.Setup(e => e.GetRecentFilingsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([]);
        _alerts.Setup(a => a.GenerateReviewDueAlertAsync(CoverageHarness.UserId, It.IsAny<ReviewDue>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ReviewDue, CancellationToken>((_, review, _) => _reviews.Add(review))
            .Returns(Task.CompletedTask);
        _scorer.Setup(s => s.Handle(It.IsAny<ScoreCandidateCommand>(), It.IsAny<CancellationToken>()))
            .Returns<ScoreCandidateCommand, CancellationToken>(async (command, _) =>
            {
                _rescored.Add(command.Ticker);
                var candidate = await _h.Db.OpportunityCandidates.IgnoreQueryFilters()
                    .FirstAsync(c => c.Ticker == command.Ticker && c.Status == CandidateStatus.Active);
                _h.Db.CandidateScores.Add(new CandidateScore
                {
                    CandidateId = candidate.Id, ScoredAt = _clock.Now, StructureScore = 70, FundamentalsScore = 70,
                });
                await _h.Db.SaveChangesAsync();
                return new ScoreCandidateResult(candidate.Id, command.Ticker, false, null!);
            });
    }

    private CoverageEventReviewer Reviewer => new(
        _h.Coverage, _scorer.Object, _h.Evaluator, _clock, NullLogger<CoverageEventReviewer>.Instance);

    private LookaheadUniverse Universe => new(
        _h.Brokerage.Object, _h.Watchlist.Object, new ThesisRepository(_h.Db), _crypto.Object, _h.Coverage);

    private FilingWatchJob FilingJob => new(
        _h.Banking.Object, Universe, _edgar.Object, _alerts.Object,
        new ReviewDueRaiser(new ThesisRepository(_h.Db), new CandidateRepository(_h.Db), _alerts.Object, Reviewer),
        NullLogger<FilingWatchJob>.Instance);

    private void Filing(string ticker, string accession = "0001-26-000001")
        => _edgar.Setup(e => e.GetRecentFilingsAsync(ticker, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .ReturnsAsync([new EdgarFiling(ticker, "10-Q", Today, null, "10-Q", accession, $"https://sec.test/{accession}", true)]);

    private async Task<List<CoverageEvent>> EventsAsync()
        => await _h.Db.CoverageEvents.IgnoreQueryFilters().AsNoTracking().OrderBy(e => e.Ticker).ToListAsync();

    private async Task<CoverageMember> CoveredCandidateAsync(string ticker, string verdict = "Wait")
    {
        await _h.CandidateAsync(ticker, scoredAt: _clock.Now.AddDays(-1));
        var member = await _h.MemberAsync(ticker, addedAt: _clock.Now.AddDays(-10));
        _h.Verdicts.Verdicts[ticker] = verdict;
        return member;
    }

    [Fact]
    public async Task A_filing_on_a_coverage_name_moves_LastEventAt_raises_the_candidate_notice_and_rescores_once()
    {
        await CoveredCandidateAsync("MU");
        Filing("MU");

        await FilingJob.ExecuteAsync(MondayUtc);

        var member = (await _h.MembersAsync()).Single();
        member.LastEventAt.Should().Be(_clock.Now);

        var review = _reviews.Should().ContainSingle().Subject;
        review.Subject.Should().Be(ReviewSubjectKind.Candidate);
        review.Source.Should().Be(ReviewDueSource.Filing);
        review.Reference.Should().Be("0001-26-000001");

        _rescored.Should().Equal("MU");
        var evt = (await EventsAsync()).Should().ContainSingle().Subject;
        evt.Ticker.Should().Be("MU");
        evt.Source.Should().Be(ReviewDueSource.Filing);
        evt.Disposition.Should().Be(CoverageEventDisposition.Kept);
        evt.Rescored.Should().BeTrue();
        evt.ReviewNotice.Should().Be("CandidateReviewDue");
        evt.Verdict.Should().Be("Wait");
        evt.ReviewedAt.Should().Be(_clock.Now);

        // A coverage name that is not otherwise in the alert universe earns no FilingLanded alert of its own.
        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_repeat_of_the_same_filing_is_idempotent_one_event_one_rescore_and_a_stable_notice_identity()
    {
        await CoveredCandidateAsync("MU");
        Filing("MU");

        await FilingJob.ExecuteAsync(MondayUtc);
        _clock.Now = _clock.Now.AddHours(1);
        await FilingJob.ExecuteAsync(MondayUtc.AddHours(1));

        (await EventsAsync()).Should().ContainSingle();
        _rescored.Should().ContainSingle();
        (await _h.MembersAsync()).Single().LastEventAt.Should().Be(new DateTimeOffset(MondayUtc), "the repeat moves nothing");

        // The detector hands the generator the same identity both times, so its once-only dedup absorbs the repeat.
        _reviews.Should().HaveCount(2);
        _reviews.Select(r => (r.SubjectId, r.Source, r.Reference)).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task A_new_filing_on_the_same_name_is_a_new_event()
    {
        await CoveredCandidateAsync("MU");
        Filing("MU", "0001-26-000001");
        await FilingJob.ExecuteAsync(MondayUtc);

        _clock.Now = _clock.Now.AddHours(2);
        Filing("MU", "0001-26-000002");
        await FilingJob.ExecuteAsync(MondayUtc.AddHours(2));

        (await EventsAsync()).Select(e => e.Reference).Should().BeEquivalentTo("0001-26-000001", "0001-26-000002");
        _rescored.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_held_coverage_name_with_no_candidate_is_recorded_without_a_rescore_and_keeps_its_filing_alert()
    {
        _h.Hold("NVDA");
        await _h.MemberAsync("NVDA", CoverageReason.Held, _clock.Now.AddDays(-10));
        Filing("NVDA");

        await FilingJob.ExecuteAsync(MondayUtc);

        var evt = (await EventsAsync()).Should().ContainSingle().Subject;
        evt.Disposition.Should().Be(CoverageEventDisposition.Kept);
        evt.Rescored.Should().BeFalse();
        evt.ReviewNotice.Should().BeNull("a held name with no thesis and no candidate has nothing to review");
        evt.Detail.Should().Contain("No Active candidate");
        _rescored.Should().BeEmpty();
        (await _h.MembersAsync()).Single().LastEventAt.Should().Be(_clock.Now);
        _reviews.Should().BeEmpty();
        _alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            CoverageHarness.UserId, "NVDA", "10-Q", Today, "0001-26-000001", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_hit_on_a_name_that_is_not_covered_records_nothing()
    {
        await _h.CandidateAsync("AMD");
        Filing("AMD");

        await FilingJob.ExecuteAsync(MondayUtc);

        (await EventsAsync()).Should().BeEmpty();
        _rescored.Should().BeEmpty();
        _reviews.Should().ContainSingle("the candidate's own notice is unchanged");
    }

    [Fact]
    public async Task A_thesis_name_in_coverage_raises_the_thesis_notice_and_is_recorded()
    {
        var thesis = await _h.ThesisAsync("MU");
        await _h.MemberAsync("MU", CoverageReason.Screen, _clock.Now.AddDays(-10));
        Filing("MU");

        await FilingJob.ExecuteAsync(MondayUtc);

        _reviews.Should().ContainSingle().Which.Should().Match<ReviewDue>(r =>
            r.Subject == ReviewSubjectKind.Thesis && r.SubjectId == thesis.Id);
        (await EventsAsync()).Should().ContainSingle().Which.ReviewNotice.Should().Be("ThesisReviewDue");
    }

    [Fact]
    public async Task The_review_applies_the_remove_rules_and_journals_the_rule_that_fired()
    {
        await CoveredCandidateAsync("XYZ", verdict: "Avoid");
        await _h.Coverage.AddObservationUnscopedAsync(new CoverageVerdictObservation
        {
            UserId = CoverageHarness.UserId, Ticker = "XYZ", ObservedOn = Today.AddDays(-1), Verdict = "Avoid", FiredRuleId = "x",
        });
        Filing("XYZ");

        await FilingJob.ExecuteAsync(MondayUtc);

        (await _h.MembersAsync()).Should().BeEmpty("two consecutive Avoid readings remove an unprotected name");
        var evt = (await EventsAsync()).Should().ContainSingle().Subject;
        evt.Disposition.Should().Be(CoverageEventDisposition.Removed);
        evt.Rule.Should().Be(CoverageRuleIds.AvoidTwice);
        evt.Verdict.Should().Be("Avoid");
        var journal = (await _h.JournalAsync()).Should().ContainSingle().Subject;
        journal.Action.Should().Be(CoverageAction.Removed);
        journal.Rule.Should().Be(CoverageRuleIds.AvoidTwice);
    }

    [Fact]
    public async Task A_name_removed_by_a_review_rejoins_only_on_evidence_newer_than_the_removal()
    {
        await CoveredCandidateAsync("XYZ", verdict: "Avoid");
        foreach (var (days, word) in new[] { (-3, "Buy"), (-2, "Buy"), (-1, "Avoid") })
        {
            await _h.Coverage.AddObservationUnscopedAsync(new CoverageVerdictObservation
            {
                UserId = CoverageHarness.UserId, Ticker = "XYZ", ObservedOn = Today.AddDays(days), Verdict = word, FiredRuleId = "x",
            });
        }

        Filing("XYZ");
        await FilingJob.ExecuteAsync(MondayUtc);
        (await _h.MembersAsync()).Should().BeEmpty();

        // The same night's evaluation sees two older Buy days but a removal after them: no re-add.
        await _h.RunAsync(_clock.Now);
        (await _h.MembersAsync()).Should().BeEmpty();

        // Newer evidence: Buy readings on two days after the removal.
        _h.Verdicts.Verdicts["XYZ"] = "Buy";
        await _h.RunAsync(_clock.Now.AddDays(1));
        (await _h.MembersAsync()).Should().BeEmpty("one Buy day after the removal is not yet two");
        await _h.RunAsync(_clock.Now.AddDays(2));

        (await _h.TickersAsync()).Should().Equal("XYZ");
        (await _h.JournalAsync()).Select(e => e.Action).Should().Equal(CoverageAction.Removed, CoverageAction.Added);
    }

    [Fact]
    public async Task A_failed_rescore_is_recorded_and_the_rules_still_run()
    {
        await CoveredCandidateAsync("MU");
        _scorer.Setup(s => s.Handle(It.IsAny<ScoreCandidateCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));
        Filing("MU");

        await FilingJob.ExecuteAsync(MondayUtc);

        var evt = (await EventsAsync()).Should().ContainSingle().Subject;
        evt.Rescored.Should().BeFalse();
        evt.Disposition.Should().Be(CoverageEventDisposition.Kept);
        evt.Detail.Should().Contain("Re-score failed");
        (await _h.MembersAsync()).Single().LastEventAt.Should().Be(_clock.Now);
    }

    [Fact]
    public async Task An_event_resets_the_idle_clock_so_the_no_event_rule_does_not_remove_the_name()
    {
        // 61 days idle and no catalyst ahead: without the event the no-event rule removes it.
        await _h.MemberAsync("MU", addedAt: _clock.Now.AddDays(-61));
        Filing("MU");

        await FilingJob.ExecuteAsync(MondayUtc);
        await _h.RunAsync(_clock.Now.AddDays(5));

        (await _h.TickersAsync()).Should().Equal("MU");
        (await _h.JournalAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_digest_lists_the_events_since_a_time_with_their_dispositions()
    {
        await CoveredCandidateAsync("MU");
        await CoveredCandidateAsync("XYZ", verdict: "Avoid");
        await _h.Coverage.AddObservationUnscopedAsync(new CoverageVerdictObservation
        {
            UserId = CoverageHarness.UserId, Ticker = "XYZ", ObservedOn = Today.AddDays(-1), Verdict = "Avoid", FiredRuleId = "x",
        });
        Filing("MU");
        Filing("XYZ", "0002-26-000002");
        await FilingJob.ExecuteAsync(MondayUtc);

        var handler = new ListCoverageEventsQueryHandler(
            _h.Coverage, _h.Snapshots, _clock, Microsoft.Extensions.Options.Options.Create(_h.Options));

        var digest = await handler.Handle(
            new ListCoverageEventsQuery(CoverageHarness.UserId, _clock.Now.AddHours(-1)), default);

        digest.Total.Should().Be(2);
        digest.Removed.Should().Be(1);
        digest.Rescored.Should().Be(2);
        digest.Truncated.Should().BeFalse();
        digest.Events.Select(e => (e.Ticker, e.Disposition, e.Rule)).Should().BeEquivalentTo(
            new[] { ("MU", "Kept", (string?)null), ("XYZ", "Removed", (string?)CoverageRuleIds.AvoidTwice) });
        digest.Events.Single(e => e.Ticker == "MU").ReviewNotice.Should().Be("CandidateReviewDue");
        digest.Changes.Should().ContainSingle().Which.Rule.Should().Be(CoverageRuleIds.AvoidTwice);

        // A pulse reading again after the events: nothing new.
        (await handler.Handle(new ListCoverageEventsQuery(CoverageHarness.UserId, _clock.Now.AddMinutes(1)), default))
            .Events.Should().BeEmpty();

        // Capped: the total still counts every match.
        var capped = await handler.Handle(new ListCoverageEventsQuery(CoverageHarness.UserId, _clock.Now.AddHours(-1), 1), default);
        capped.Events.Should().ContainSingle();
        capped.Total.Should().Be(2);
        capped.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task The_nightly_evaluation_prunes_events_older_than_the_retention()
    {
        _h.Db.CoverageEvents.Add(new CoverageEvent
        {
            UserId = CoverageHarness.UserId, Ticker = "OLD", Source = "filing", Reference = "a",
            OccurredAt = _clock.Now.AddDays(-(_h.Options.EventRetentionDays + 1)),
        });
        _h.Db.CoverageEvents.Add(new CoverageEvent
        {
            UserId = CoverageHarness.UserId, Ticker = "NEW", Source = "filing", Reference = "b", OccurredAt = _clock.Now.AddDays(-1),
        });
        await _h.Db.SaveChangesAsync();

        await _h.Evaluator.EvaluateAllAsync(_clock.Now);

        (await EventsAsync()).Select(e => e.Ticker).Should().Equal("NEW");
    }
}
