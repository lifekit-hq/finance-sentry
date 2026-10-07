namespace FinanceSentry.Modules.Companion.Tests;

using System.Text.Json;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FluentAssertions;
using Xunit;

/// <summary>Spec 859 FR-003 / SC-003: the lock-screen notification is headline only, never amounts or merchants.</summary>
public sealed class PushPayloadTests
{
    private static CompanionEvent Event(CompanionEventKind kind, string subject, string summary, string? appPath = null) => new()
    {
        AppPath = appPath,
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

    private static string? Url(CompanionEvent evt)
    {
        using var doc = JsonDocument.Parse(PushPayload.Build(evt));
        return doc.RootElement.GetProperty("notification").GetProperty("data").GetProperty("onActionClick")
            .GetProperty("default").GetProperty("url").GetString();
    }

    [Theory]
    [InlineData("/assets/NVDA")]
    [InlineData("/transactions?type=debit&category=Dining%20out&from=2026-03-01&to=2026-03-31")]
    [InlineData("/accounts/list")]
    public void The_tap_opens_the_thing_the_event_is_about(string appPath)
    {
        var evt = Event(CompanionEventKind.UnusualSpend, "Dining out", "x", appPath);

        Url(evt).Should().Be(appPath);
    }

    [Fact]
    public void An_event_with_no_target_opens_the_alerts_page()
        => Url(Event(CompanionEventKind.OperationalFailure, "SyncJob", "x")).Should().Be("/alerts");

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("https://evil.example/assets/NVDA")]
    [InlineData("//evil.example/assets/NVDA")]
    [InlineData("assets/NVDA")]
    public void A_target_that_is_not_a_path_inside_the_app_falls_back_to_the_alerts_page(string appPath)
        => Url(Event(CompanionEventKind.RiskViolation, "NVDA", "x", appPath)).Should().Be("/alerts");

    [Fact]
    public void A_target_with_a_filter_adds_no_amount_or_merchant_to_the_lock_screen()
    {
        var json = PushPayload.Build(Event(CompanionEventKind.UnusualSpend, "Groceries", "Spent $1,234.56 at Starbucks",
            "/transactions?type=debit&category=Groceries&from=2026-03-01"));

        json.Should().NotContain("1,234.56").And.NotContain("Starbucks");
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
