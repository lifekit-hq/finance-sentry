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
    [InlineData("CoverageGap", CompanionEventKind.CoverageGap)]
    [InlineData("UnusualSpend", CompanionEventKind.UnusualSpend)]
    [InlineData("Opportunity", CompanionEventKind.Opportunity)]
    [InlineData("ThesisBroken", CompanionEventKind.ThesisBreak)]
    [InlineData("MarketStructure", CompanionEventKind.MarketStructure)]
    [InlineData("LowBalance", CompanionEventKind.LowBalance)]
    [InlineData("CashShortfall", CompanionEventKind.CashShortfall)]
    [InlineData("PerformanceBrief", CompanionEventKind.PerformanceBrief)]
    [InlineData("BudgetBreach", CompanionEventKind.BudgetBreach)]
    [InlineData("FamilyStatement", CompanionEventKind.FamilyStatement)]
    [InlineData("FireBrief", CompanionEventKind.FireBrief)]
    [InlineData("PolicyReview", CompanionEventKind.PolicyReview)]
    [InlineData("PolicyReviewMissed", CompanionEventKind.PolicyReviewMissed)]
    [InlineData("RelativeUnderperformance", CompanionEventKind.RelativeUnderperformance)]
    [InlineData("MacroEventAhead", CompanionEventKind.MacroEventAhead)]
    [InlineData("MacroEventReleased", CompanionEventKind.MacroEventReleased)]
    [InlineData("ConnectionDormant", CompanionEventKind.ConnectionDormant)]
    [InlineData("AccountRetired", CompanionEventKind.AccountRetired)]
    [InlineData("ThesisReviewDue", CompanionEventKind.ThesisReviewDue)]
    [InlineData("CandidateReviewDue", CompanionEventKind.CandidateReviewDue)]
    public void Known_alert_types_map_to_kinds(string alertType, CompanionEventKind expected)
        => _policy.ClassifyAlert(alertType).Should().Be(expected);

    [Theory]
    [InlineData("MarketStructure", NotificationMode.Realtime)]
    [InlineData("NewsCluster", NotificationMode.Realtime)]
    [InlineData("MarketStructure", NotificationMode.Scan)]
    public void Digest_only_alert_types_reach_the_digest_not_a_wake_in_every_live_mode(string alertType, NotificationMode mode)
        => _policy.DispositionFor(mode, _policy.ClassifyAlert(alertType)!.Value).Should().Be(EventDisposition.HeldForDigest);

    [Fact]
    public void Decision_grade_alert_types_stay_off_the_digest_lane()
    {
        foreach (var type in new[] { "PolicyViolation", "ThesisBroken", "RelativeUnderperformance", "RebalanceProposal", "CashShortfall" })
            _policy.DispositionFor(NotificationMode.Realtime, _policy.ClassifyAlert(type)!.Value)
                .Should().Be(EventDisposition.Pending, type);
    }

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

    [Fact]
    public void Alert_id_round_trips_through_its_dedup_key_and_other_keys_yield_nothing()
    {
        var alertId = Guid.NewGuid();

        _policy.AlertIdFromDedupKey(_policy.AlertDedupKey(alertId)).Should().Be(alertId);
        _policy.AlertIdFromDedupKey(_policy.AnalystDedupKey(Guid.NewGuid(), Guid.NewGuid())).Should().BeNull();
        _policy.AlertIdFromDedupKey("alert:not-a-guid").Should().BeNull();
        _policy.AlertIdFromDedupKey(string.Empty).Should().BeNull();
    }
}
