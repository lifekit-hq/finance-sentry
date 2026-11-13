namespace FinanceSentry.Tests.Unit.Alerts;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>A thesis review notice carries the thesis's narrative falsifiers on its message tail and its companion summary.</summary>
public sealed class ReviewDueNarrativeFalsifiersTests
{
    private static readonly Guid ThesisId = Guid.Parse("3f2b8c1e-5a7d-4e90-9b1c-0d4e6a8f2c11");
    private const int SummaryCap = 500;

    private static ReviewDue Review(IReadOnlyList<string>? falsifiers, IReadOnlyList<string>? evidence = null) => new(
        ReviewSubjectKind.Thesis, ThesisId, "CBRS", ReviewDueSource.Filing, "0001-26-000123",
        evidence ?? ["filing:0001-26-000123"], "CBRS: 10-Q filed", falsifiers);

    private static List<string> Entries(int count, int length)
        => Enumerable.Range(0, count).Select(i => $"F{i:D2}" + new string('x', length - 3)).ToList();

    private static List<string> SummaryFalsifiers(string summary)
    {
        var field = summary.Split(" | ").SingleOrDefault(f => f.StartsWith("falsifiers=", StringComparison.Ordinal));
        return field is null ? [] : [.. field["falsifiers=".Length..].Split('|')];
    }

    private static void AssertMarker(IReadOnlyList<string> shown, int total)
    {
        var marker = shown[^1];
        var match = Regex.Match(marker, $@"^\+(\d+)_more,full_list_on_thesis:{ThesisId:D}$");
        match.Success.Should().BeTrue($"the last item must be the truncation marker, was '{marker}'");
        (shown.Count - 1 + int.Parse(match.Groups[1].Value)).Should().Be(total, "every entry is either shown (whole or cut) or counted as omitted");
    }

    [Fact]
    public void The_message_tail_round_trips_the_falsifiers()
    {
        var message = ReviewDueMessage.Compose(Review(["Loses_the_contract", "CFO_departs"]));

        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        parsed.NarrativeFalsifiers.Should().Equal("Loses_the_contract", "CFO_departs");
        parsed.Evidence.Should().Equal("filing:0001-26-000123");
    }

    [Fact]
    public void Whitespace_in_a_falsifier_is_kept_inside_the_tail_grammar()
    {
        var message = ReviewDueMessage.Compose(Review(["Loses the contract"]));

        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        parsed.NarrativeFalsifiers.Should().Equal("Loses_the_contract");
    }

    [Fact]
    public void A_notice_without_falsifiers_has_no_falsifier_field_and_parses_to_none()
    {
        var message = ReviewDueMessage.Compose(Review(null));

        message.Should().NotContain("falsifiers=");
        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        parsed.NarrativeFalsifiers.Should().BeNull();
    }

    [Fact]
    public void Under_the_length_cap_evidence_is_dropped_before_the_falsifiers()
    {
        var evidence = Enumerable.Range(0, 30).Select(i => $"article:{i:D40}").ToList();

        var message = ReviewDueMessage.Compose(Review(["Loses_the_contract"], evidence));

        message.Length.Should().BeLessThanOrEqualTo(ReviewDueMessage.MaxMessageLength);
        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        parsed.NarrativeFalsifiers.Should().Equal("Loses_the_contract");
    }

    [Fact]
    public void The_companion_summary_lists_the_falsifiers_before_the_evidence()
    {
        var summary = ReviewDueMessage.Summarize(Review(["Loses the contract"]), 500);

        summary.Should().Contain(" | falsifiers=Loses_the_contract");
        summary.IndexOf("falsifiers=", StringComparison.Ordinal)
            .Should().BeLessThan(summary.IndexOf("evidence=", StringComparison.Ordinal));
    }

    [Fact]
    public void The_summary_keeps_every_falsifier_whole_and_adds_no_marker_when_they_fit()
    {
        var summary = ReviewDueMessage.Summarize(Review(["Loses_the_contract", "CFO_departs"]), SummaryCap);

        SummaryFalsifiers(summary).Should().Equal("Loses_the_contract", "CFO_departs");
        summary.Should().NotContain("full_list_on_thesis");
    }

    [Fact]
    public void The_summary_cuts_one_very_long_falsifier_with_an_ellipsis_and_marks_it()
    {
        var summary = ReviewDueMessage.Summarize(Review([new string('y', 500)]), SummaryCap);

        var shown = SummaryFalsifiers(summary);
        summary.Length.Should().BeLessThanOrEqualTo(SummaryCap);
        shown.Should().HaveCount(2);
        shown[0].Should().EndWith("…").And.StartWith("yyyy");
        shown[0].Length.Should().BeLessThan(500);
        AssertMarker(shown, 1);
        summary.Should().Contain($"subject=thesis:{ThesisId:D}");
    }

    [Fact]
    public void The_summary_keeps_what_fits_of_three_200_character_falsifiers_in_order_and_marks_the_rest()
    {
        var entries = Entries(3, 200);

        var summary = ReviewDueMessage.Summarize(Review(entries), SummaryCap);

        var shown = SummaryFalsifiers(summary);
        summary.Length.Should().BeLessThanOrEqualTo(SummaryCap);
        shown[0].Should().Be(entries[0]);
        shown.Should().HaveCountGreaterThan(2);
        shown[1].Should().StartWith("F01").And.EndWith("…");
        AssertMarker(shown, 3);
        shown[^1].Should().StartWith("+1_more");
    }

    [Fact]
    public void The_summary_never_drops_twenty_100_character_falsifiers_silently()
    {
        var entries = Entries(20, 100);

        var summary = ReviewDueMessage.Summarize(Review(entries), SummaryCap);

        var shown = SummaryFalsifiers(summary);
        summary.Length.Should().BeLessThanOrEqualTo(SummaryCap);
        shown.Take(2).Should().Equal(entries.Take(2));
        AssertMarker(shown, 20);
    }

    [Fact]
    public void The_message_keeps_every_falsifier_and_adds_no_marker_when_they_fit()
    {
        var entries = Entries(5, 100);

        var message = ReviewDueMessage.Compose(Review(entries));

        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        parsed.NarrativeFalsifiers.Should().Equal(entries);
        message.Should().NotContain("full_list_on_thesis");
    }

    [Fact]
    public void The_message_cuts_twenty_100_character_falsifiers_to_the_alert_cap_and_marks_the_rest()
    {
        var entries = Entries(20, 100);

        var message = ReviewDueMessage.Compose(Review(entries));

        message.Length.Should().BeLessThanOrEqualTo(ReviewDueMessage.MaxMessageLength);
        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        var shown = parsed.NarrativeFalsifiers!;
        shown.Take(5).Should().Equal(entries.Take(5));
        AssertMarker(shown, 20);
        parsed.SubjectId.Should().Be(ThesisId);
        parsed.Ticker.Should().Be("CBRS");
        message.Should().StartWith("CBRS: 10-Q filed");
    }

    [Fact]
    public void The_message_cuts_one_very_long_falsifier_with_an_ellipsis_and_marks_it()
    {
        var message = ReviewDueMessage.Compose(Review([new string('z', 1500)]), 400);

        message.Length.Should().BeLessThanOrEqualTo(400);
        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        var shown = parsed.NarrativeFalsifiers!;
        shown[0].Should().EndWith("…");
        AssertMarker(shown, 1);
    }

    [Fact]
    public void The_message_drops_evidence_before_it_touches_the_falsifiers()
    {
        var entries = Entries(4, 100);
        var evidence = Enumerable.Range(0, 30).Select(i => $"article:{i:D40}").ToList();

        var message = ReviewDueMessage.Compose(Review(entries, evidence));

        message.Length.Should().BeLessThanOrEqualTo(ReviewDueMessage.MaxMessageLength);
        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        parsed.NarrativeFalsifiers.Should().Equal(entries);
    }

    [Fact]
    public void A_composed_then_parsed_then_summarised_list_reports_the_real_omitted_count()
    {
        var entries = Entries(20, 100);

        var message = ReviewDueMessage.Compose(Review(entries));
        ReviewDueMessage.TryParse(message, out var parsed).Should().BeTrue();
        var summary = ReviewDueMessage.Summarize(parsed, SummaryCap);

        var shown = SummaryFalsifiers(summary);
        summary.Length.Should().BeLessThanOrEqualTo(SummaryCap);
        shown.Take(2).Should().Equal(entries.Take(2));
        shown.Count(f => f.Contains("_more,", StringComparison.Ordinal)).Should().Be(1, "the earlier marker is merged, not repeated or cut");
        AssertMarker(shown, 20);
    }

    [Fact]
    public void A_carried_marker_survives_a_summary_budget_that_would_otherwise_fit_the_rest()
    {
        var entries = Entries(20, 100);
        ReviewDueMessage.TryParse(ReviewDueMessage.Compose(Review(entries)), out var parsed).Should().BeTrue();
        var composedShown = parsed.NarrativeFalsifiers!;

        var summary = ReviewDueMessage.Summarize(Review(composedShown.Take(2).Append(composedShown[^1]).ToList()), 1000);

        var shown = SummaryFalsifiers(summary);
        shown.Take(2).Should().Equal(entries.Take(2));
        AssertMarker(shown, 2 + int.Parse(Regex.Match(composedShown[^1], @"^\+(\d+)_").Groups[1].Value));
    }
}
