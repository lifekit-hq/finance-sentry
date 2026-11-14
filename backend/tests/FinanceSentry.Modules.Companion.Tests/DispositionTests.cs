namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FluentAssertions;
using Xunit;

/// <summary>Mode → disposition mapping (feature 031, US2, T024).</summary>
public sealed class DispositionTests
{
    private readonly MaterialityPolicy _policy = new();

    [Theory]
    [InlineData(NotificationMode.Quiet, EventDisposition.SuppressedByMode)]
    [InlineData(NotificationMode.Digest, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Scan, EventDisposition.Pending)]
    [InlineData(NotificationMode.Realtime, EventDisposition.Pending)]
    public void Mode_maps_to_disposition(NotificationMode mode, EventDisposition expected)
        => _policy.DispositionForMode(mode).Should().Be(expected);

    [Theory]
    [InlineData(NotificationMode.Realtime, null, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Realtime, 23.0, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Realtime, 24.0, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Realtime, 24.5, EventDisposition.Pending)]
    [InlineData(NotificationMode.Scan, null, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Scan, 48.0, EventDisposition.Pending)]
    [InlineData(NotificationMode.Digest, 48.0, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Quiet, null, EventDisposition.SuppressedByMode)]
    [InlineData(NotificationMode.Quiet, 48.0, EventDisposition.SuppressedByMode)]
    public void Sync_failure_rides_the_digest_unless_the_source_is_stale_past_a_day(
        NotificationMode mode, double? stalenessHours, EventDisposition expected)
    {
        TimeSpan? staleness = stalenessHours is { } h ? TimeSpan.FromHours(h) : null;

        _policy.DispositionFor(mode, CompanionEventKind.SyncFailure, staleness).Should().Be(expected);
    }

    [Theory]
    [InlineData(CompanionEventKind.ThesisBreak)]
    [InlineData(CompanionEventKind.LowBalance)]
    [InlineData(CompanionEventKind.Opportunity)]
    [InlineData(CompanionEventKind.PriceHike)]
    [InlineData(CompanionEventKind.FilingLanded)]
    public void Other_kinds_keep_the_mode_disposition_regardless_of_staleness(CompanionEventKind kind)
    {
        _policy.DispositionFor(NotificationMode.Realtime, kind).Should().Be(EventDisposition.Pending);
        _policy.DispositionFor(NotificationMode.Realtime, kind, TimeSpan.FromHours(1)).Should().Be(EventDisposition.Pending);
    }

    [Theory]
    [InlineData(CompanionEventKind.AnalystAction)]
    [InlineData(CompanionEventKind.EarningsAhead)]
    [InlineData(CompanionEventKind.FamilyStatement)]
    [InlineData(CompanionEventKind.FireBrief)]
    [InlineData(CompanionEventKind.MarketStructure)]
    [InlineData(CompanionEventKind.NewsCluster)]
    public void Digest_only_kinds_are_held_for_the_digest_in_every_waking_mode(CompanionEventKind kind)
    {
        foreach (var mode in new[] { NotificationMode.Digest, NotificationMode.Scan, NotificationMode.Realtime })
            _policy.DispositionFor(mode, kind).Should().Be(EventDisposition.HeldForDigest);
    }

    [Theory]
    [InlineData(CompanionEventKind.AnalystAction)]
    [InlineData(CompanionEventKind.EarningsAhead)]
    [InlineData(CompanionEventKind.FamilyStatement)]
    [InlineData(CompanionEventKind.FireBrief)]
    [InlineData(CompanionEventKind.MarketStructure)]
    [InlineData(CompanionEventKind.NewsCluster)]
    public void Digest_only_kinds_stay_silent_under_quiet(CompanionEventKind kind)
        => _policy.DispositionFor(NotificationMode.Quiet, kind).Should().Be(EventDisposition.SuppressedByMode);

    [Theory]
    [InlineData(CompanionEventKind.ThesisReviewDue, "filing")]
    [InlineData(CompanionEventKind.ThesisReviewDue, "break")]
    [InlineData(CompanionEventKind.ThesisReviewDue, "unbreak")]
    [InlineData(CompanionEventKind.CandidateReviewDue, "filing")]
    [InlineData(CompanionEventKind.CandidateReviewDue, "break")]
    [InlineData(CompanionEventKind.CandidateReviewDue, "unbreak")]
    public void Review_notices_raised_by_a_filing_break_or_unbreak_keep_the_mode_rule(CompanionEventKind kind, string source)
    {
        foreach (var mode in new[] { NotificationMode.Scan, NotificationMode.Realtime })
            _policy.DispositionFor(mode, kind, reviewSource: source).Should().Be(EventDisposition.Pending);

        _policy.DispositionFor(NotificationMode.Digest, kind, reviewSource: source).Should().Be(EventDisposition.HeldForDigest);
        _policy.DispositionFor(NotificationMode.Quiet, kind, reviewSource: source).Should().Be(EventDisposition.SuppressedByMode);
    }

    [Theory]
    [InlineData(CompanionEventKind.ThesisReviewDue, "news")]
    [InlineData(CompanionEventKind.ThesisReviewDue, "earnings")]
    [InlineData(CompanionEventKind.ThesisReviewDue, "catalyst")]
    [InlineData(CompanionEventKind.CandidateReviewDue, "news")]
    [InlineData(CompanionEventKind.CandidateReviewDue, "earnings")]
    [InlineData(CompanionEventKind.CandidateReviewDue, "catalyst")]
    public void Review_notices_raised_by_news_earnings_or_a_catalyst_ride_the_digest_lane(CompanionEventKind kind, string source)
    {
        foreach (var mode in new[] { NotificationMode.Digest, NotificationMode.Scan, NotificationMode.Realtime })
            _policy.DispositionFor(mode, kind, reviewSource: source).Should().Be(EventDisposition.HeldForDigest);

        _policy.DispositionFor(NotificationMode.Quiet, kind, reviewSource: source).Should().Be(EventDisposition.SuppressedByMode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rumour")]
    public void A_review_notice_whose_source_cannot_be_read_is_held_rather_than_waking_Ledger_on_a_guess(string? source)
        => _policy.DispositionFor(NotificationMode.Realtime, CompanionEventKind.ThesisReviewDue, reviewSource: source)
            .Should().Be(EventDisposition.HeldForDigest);

    [Fact]
    public void Review_kinds_are_their_own_kinds_never_the_batched_news_or_filing_kinds()
    {
        var kinds = new[] { "ThesisReviewDue", "CandidateReviewDue", "NewsCluster", "FilingLanded" }.Select(_policy.ClassifyAlert).ToList();

        kinds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Operational_failure_still_breaks_through_quiet()
        => _policy.DispositionFor(NotificationMode.Quiet, CompanionEventKind.OperationalFailure)
            .Should().Be(EventDisposition.Pending);

    [Fact]
    public void Escalation_age_is_one_day()
        => _policy.SyncFailureEscalationAge.Should().Be(TimeSpan.FromHours(24));
}
