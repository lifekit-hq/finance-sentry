namespace FinanceSentry.Tests.Unit.Alerts;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FluentAssertions;
using Xunit;

/// <summary>A thesis review notice carries the thesis's narrative falsifiers on its message tail and its companion summary.</summary>
public sealed class ReviewDueNarrativeFalsifiersTests
{
    private static ReviewDue Review(IReadOnlyList<string>? falsifiers, IReadOnlyList<string>? evidence = null) => new(
        ReviewSubjectKind.Thesis, Guid.NewGuid(), "CBRS", ReviewDueSource.Filing, "0001-26-000123",
        evidence ?? ["filing:0001-26-000123"], "CBRS: 10-Q filed", falsifiers);

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
}
