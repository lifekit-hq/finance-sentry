namespace FinanceSentry.Tests.Unit.Alerts;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FluentAssertions;
using Xunit;

/// <summary>The review notice's text form: what an alert message carries so Companion can read the payload back.</summary>
public sealed class ReviewDueMessageTests
{
    private static readonly Guid SubjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static ReviewDue Review(IReadOnlyList<string>? evidence = null, string headline = "NVDA: 10-Q filed 2026-11-12")
        => new(ReviewSubjectKind.Thesis, SubjectId, "NVDA", ReviewDueSource.Filing, "0001-26-000123",
            evidence ?? ["filing:0001-26-000123", "url:https://www.sec.gov/Archives/x.htm"], headline);

    [Fact]
    public void A_message_round_trips_its_payload()
    {
        var message = ReviewDueMessage.Compose(Review());

        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        parsed.Should().BeEquivalentTo(Review());
    }

    [Fact]
    public void The_headline_comes_first_so_the_alert_reads_as_a_sentence()
        => ReviewDueMessage.Compose(Review()).Should().StartWith("NVDA: 10-Q filed 2026-11-12 [review subject=thesis:");

    [Fact]
    public void A_candidate_round_trips_as_a_candidate()
    {
        var review = Review() with { Subject = ReviewSubjectKind.Candidate };

        ReviewDueMessage.TryParse(ReviewDueMessage.Compose(review), out var parsed).Should().BeTrue();
        parsed.Subject.Should().Be(ReviewSubjectKind.Candidate);
    }

    [Fact]
    public void Values_that_would_break_the_grammar_are_cleaned_rather_than_corrupting_it()
    {
        var review = Review(["a b", "c]d|e"]) with { Reference = "x y[1]" };

        ReviewDueMessage.TryParse(ReviewDueMessage.Compose(review), out var parsed).Should().BeTrue();
        parsed.Reference.Should().Be("x_y_1_");
        parsed.Evidence.Should().Equal("a_b", "c_d_e");
    }

    [Fact]
    public void A_message_over_the_column_limit_drops_evidence_then_headline_but_keeps_the_payload_readable()
    {
        var evidence = Enumerable.Range(0, 200).Select(i => $"article:{Guid.NewGuid()}").ToList();

        var message = ReviewDueMessage.Compose(Review(evidence, new string('h', 5000)));

        message.Length.Should().BeLessThanOrEqualTo(ReviewDueMessage.MaxMessageLength);
        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        parsed.SubjectId.Should().Be(SubjectId);
        parsed.Source.Should().Be(ReviewDueSource.Filing);
        parsed.Evidence.Should().NotBeEmpty().And.OnlyContain(e => evidence.Contains(e));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Thesis broken: NVDA")]
    [InlineData("x [review subject=thesis:not-a-guid ticker=A source=filing ref=r]")]
    [InlineData("x [review subject=widget:11111111-2222-3333-4444-555555555555 ticker=A source=filing ref=r]")]
    [InlineData("x [review subject=thesis:11111111-2222-3333-4444-555555555555 ticker=A source=filing]")]
    [InlineData("x [review subject=thesis:11111111-2222-3333-4444-555555555555 ticker=A source=filing ref=r")]
    public void Anything_that_is_not_a_well_formed_notice_does_not_parse(string? message)
        => ReviewDueMessage.TryParse(message, out _).Should().BeFalse();

    [Fact]
    public void The_summary_always_keeps_subject_source_and_reference_and_fits_its_cap()
    {
        var evidence = Enumerable.Range(0, 50).Select(i => $"article:{Guid.NewGuid()}").ToList();

        var summary = ReviewDueMessage.Summarize(Review(evidence, new string('h', 2000)), 500);

        summary.Length.Should().BeLessThanOrEqualTo(500);
        summary.Should().StartWith("Thesis review due: NVDA (filing)")
            .And.Contain($"subject=thesis:{SubjectId}").And.Contain("source=filing").And.Contain("ref=0001-26-000123")
            .And.Contain(evidence[0]);
    }

    [Fact]
    public void The_sources_split_into_a_realtime_and_a_digest_lane()
    {
        ReviewDueSource.All.Where(ReviewDueSource.IsRealtime).Should().BeEquivalentTo(
            [ReviewDueSource.Filing, ReviewDueSource.Break, ReviewDueSource.Unbreak]);
        ReviewDueSource.All.Where(s => !ReviewDueSource.IsRealtime(s)).Should().BeEquivalentTo(
            [ReviewDueSource.News, ReviewDueSource.Earnings, ReviewDueSource.Catalyst]);
        ReviewDueSource.IsRealtime(null).Should().BeFalse();
    }
}
