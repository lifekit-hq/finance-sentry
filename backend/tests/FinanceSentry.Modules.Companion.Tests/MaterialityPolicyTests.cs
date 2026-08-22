namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FluentAssertions;
using Xunit;

/// <summary>Materiality classification + dedup keys (feature 031, US2, T023).</summary>
public sealed class MaterialityPolicyTests
{
    private readonly MaterialityPolicy _policy = new();

    [Theory]
    [InlineData("PolicyViolation", CompanionEventKind.RiskViolation)]
    [InlineData("SyncFailure", CompanionEventKind.SyncFailure)]
    [InlineData("UnusualSpend", CompanionEventKind.UnusualSpend)]
    [InlineData("Opportunity", CompanionEventKind.Opportunity)]
    [InlineData("ThesisBroken", CompanionEventKind.ThesisBreak)]
    [InlineData("MarketStructure", CompanionEventKind.MarketStructure)]
    [InlineData("LowBalance", CompanionEventKind.LowBalance)]
    [InlineData("CashShortfall", CompanionEventKind.CashShortfall)]
    public void Known_alert_types_map_to_kinds(string alertType, CompanionEventKind expected)
        => _policy.ClassifyAlert(alertType).Should().Be(expected);

    [Fact]
    public void Unknown_alert_type_is_not_surfaced()
        => _policy.ClassifyAlert("SomethingElse").Should().BeNull();

    [Fact]
    public void Dedup_keys_are_stable_and_distinct()
    {
        var alertId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var userId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var actionId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        _policy.AlertDedupKey(alertId).Should().Be($"alert:{alertId}");
        _policy.AnalystDedupKey(userId, actionId).Should().Be($"analyst:{userId}:{actionId}");
        _policy.AlertDedupKey(alertId).Should().NotBe(_policy.AnalystDedupKey(userId, actionId));
    }
}
