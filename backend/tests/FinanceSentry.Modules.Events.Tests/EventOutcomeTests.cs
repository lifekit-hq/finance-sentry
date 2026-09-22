namespace FinanceSentry.Modules.Events.Tests;

using FinanceSentry.Modules.Events.Domain;
using FluentAssertions;
using Xunit;

/// <summary>Feature 049 US3: the outcome table, every disposition × verdict presence.</summary>
public sealed class EventOutcomeTests
{
    [Theory]
    [InlineData("Pending", "awaiting")]
    [InlineData("Dispatched", "awaiting")]
    [InlineData("HeldForDigest", "awaiting")]
    [InlineData("DeferredQuietHours", "awaiting")]
    [InlineData("Delivered", "silent")]
    [InlineData("SuppressedByMode", "not_delivered")]
    [InlineData("SuppressedByRateLimit", "not_delivered")]
    [InlineData("SuppressedByDedup", "not_delivered")]
    [InlineData("Failed", "not_delivered")]
    [InlineData(null, "awaiting")]
    public void Without_a_verdict_the_disposition_decides(string? disposition, string expected)
        => EventOutcome.From(disposition, null).Should().Be(expected);

    [Theory]
    [InlineData("Delivered", true, "verdict")]
    [InlineData("Delivered", false, "judged_immaterial")]
    [InlineData("Pending", true, "verdict")]
    [InlineData("SuppressedByMode", false, "judged_immaterial")]
    [InlineData(null, true, "verdict")]
    public void A_recorded_verdict_wins_over_any_disposition(string? disposition, bool notified, string expected)
        => EventOutcome.From(disposition, new EventVerdict { Verdict = "x", Notified = notified }).Should().Be(expected);

    [Fact]
    public void An_unknown_disposition_reads_as_awaiting_never_as_silence()
        => EventOutcome.From("SomethingNew", null).Should().Be(EventOutcome.Awaiting);
}
