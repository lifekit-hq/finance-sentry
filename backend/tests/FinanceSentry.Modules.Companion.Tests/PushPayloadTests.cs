namespace FinanceSentry.Modules.Companion.Tests;

using System.Text.Json;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FluentAssertions;
using Xunit;

/// <summary>Spec 859 FR-003 / SC-003: the lock-screen notification is headline only, never amounts or merchants.</summary>
public sealed class PushPayloadTests
{
    private static CompanionEvent Event(CompanionEventKind kind, string subject, string summary) => new()
    {
        Kind = kind,
        Subject = subject,
        Severity = "critical",
        Summary = summary,
        ReferenceId = Guid.NewGuid(),
        SourceModule = "alerts",
    };

    [Theory]
    [InlineData(CompanionEventKind.LowBalance, "Low balance")]
    [InlineData(CompanionEventKind.OperationalFailure, "Operational failure")]
    [InlineData(CompanionEventKind.RiskViolation, "Risk violation")]
    [InlineData(CompanionEventKind.FxSpread, "Fx spread")]
    public void The_title_is_the_kind_in_words(CompanionEventKind kind, string expected)
        => PushPayload.KindLabel(kind).Should().Be(expected);

    [Fact]
    public void Every_kind_has_a_label()
    {
        foreach (var kind in Enum.GetValues<CompanionEventKind>())
            PushPayload.KindLabel(kind).Should().NotBeNullOrWhiteSpace().And.NotContain("  ");
    }

    [Fact]
    public void The_payload_carries_the_kind_the_subject_and_a_deep_link()
    {
        var evt = Event(CompanionEventKind.LowBalance, "Monobank UAH", "Balance fell to $12.50");

        using var doc = JsonDocument.Parse(PushPayload.Build(evt));
        var notification = doc.RootElement.GetProperty("notification");

        notification.GetProperty("title").GetString().Should().Be("Low balance");
        notification.GetProperty("body").GetString().Should().Be("Monobank UAH");
        notification.GetProperty("tag").GetString().Should().Be(evt.Id.ToString());
        notification.GetProperty("data").GetProperty("onActionClick").GetProperty("default").GetProperty("url")
            .GetString().Should().Be(PushPayload.DeepLink);
    }

    [Fact]
    public void The_payload_never_carries_the_summary_the_severity_or_any_amount()
    {
        var evt = Event(CompanionEventKind.UnusualSpend, "Groceries", "Spent $1,234.56 at Starbucks, 12 EUR over budget");

        var json = PushPayload.Build(evt);

        json.Should().NotContain("1,234.56").And.NotContain("Starbucks").And.NotContain("EUR over")
            .And.NotContain("critical").And.NotContain("$");
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("notification");
        doc.RootElement.GetProperty("notification").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("title", "body", "tag", "data");
    }

    [Fact]
    public void A_blank_subject_leaves_out_the_body()
    {
        using var doc = JsonDocument.Parse(PushPayload.Build(Event(CompanionEventKind.SyncFailure, "  ", "x")));

        doc.RootElement.GetProperty("notification").TryGetProperty("body", out _).Should().BeFalse();
    }
}
