namespace FinanceSentry.Modules.Research.Tests.PolicyReviews;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FluentAssertions;
using Xunit;

/// <summary>Shape of the structured proposal a policy review records (#696).</summary>
public class PolicyReviewProposerTests
{
    private const decimal TotalUsd = 100_000m;

    private static readonly PolicyReviewStatus OnTime = new(
        "quarterly", 3, true, null, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), true, 0, false);

    [Fact]
    public void Proposal_carries_drift_for_every_sleeve_against_its_band()
    {
        var drift = Drift(
            Sleeve("Equity", 60, 55, 65, 70, "OverBand"),
            Sleeve("Bonds", 30, 25, 35, 30, "Within"));

        var proposal = PolicyReviewProposer.Propose(new InvestmentPolicyStatement(), drift, OnTime);

        proposal.Sleeves.Should().BeEquivalentTo(
        [
            new PolicyReviewSleeve("Equity", 60, 55, 65, 70, 70_000, 10, "OverBand"),
            new PolicyReviewSleeve("Bonds", 30, 25, 35, 30, 30_000, 0, "Within"),
        ]);
    }

    [Fact]
    public void Over_band_sleeve_is_trimmed_back_to_target_with_rationale()
    {
        var drift = Drift(Sleeve("Equity", 60, 55, 65, 70, "OverBand"));

        var proposal = PolicyReviewProposer.Propose(new InvestmentPolicyStatement(), drift, OnTime);

        var adjustment = proposal.Adjustments.Should().ContainSingle().Subject;
        adjustment.AssetClass.Should().Be("Equity");
        adjustment.Action.Should().Be(PolicyReviewAdjustmentAction.Trim);
        adjustment.ApproxAmountUsd.Should().Be(10_000m);
        adjustment.Rationale.Should().Contain("70%").And.Contain("60%").And.Contain("above its band");
    }

    [Fact]
    public void Under_band_sleeve_is_added_to_and_funded_from_contributions_first_when_the_policy_says_so()
    {
        var drift = Drift(Sleeve("Bonds", 30, 25, 35, 20, "UnderBand"));
        var ips = new InvestmentPolicyStatement { RebalancingRule = RebalancingRule.Default with { ContributionsFirst = true } };

        var proposal = PolicyReviewProposer.Propose(ips, drift, OnTime);

        var adjustment = proposal.Adjustments.Should().ContainSingle().Subject;
        adjustment.Action.Should().Be(PolicyReviewAdjustmentAction.Add);
        adjustment.ApproxAmountUsd.Should().Be(10_000m);
        adjustment.Rationale.Should().Contain("below its band").And.Contain("contributions");
    }

    [Fact]
    public void Under_band_sleeve_without_contributions_first_has_no_funding_note()
    {
        var drift = Drift(Sleeve("Bonds", 30, 25, 35, 20, "UnderBand"));
        var ips = new InvestmentPolicyStatement { RebalancingRule = RebalancingRule.Default with { ContributionsFirst = false } };

        var proposal = PolicyReviewProposer.Propose(ips, drift, OnTime);

        proposal.Adjustments.Single().Rationale.Should().NotContain("contributions");
    }

    [Fact]
    public void Material_unplanned_holding_is_flagged_for_a_policy_decision()
    {
        var drift = Drift(
            Sleeve("Crypto", 0, 0, 0, 5, "Unplanned"),
            Sleeve("Commodities", 0, 0, 0, 0.5m, "Unplanned"));

        var proposal = PolicyReviewProposer.Propose(new InvestmentPolicyStatement(), drift, OnTime);

        var adjustment = proposal.Adjustments.Should().ContainSingle("a sub-1% unplanned holding is noise").Subject;
        adjustment.AssetClass.Should().Be("Crypto");
        adjustment.Action.Should().Be(PolicyReviewAdjustmentAction.Review);
        adjustment.ApproxAmountUsd.Should().Be(5_000m);
        adjustment.Rationale.Should().Contain("no target in the policy statement");
    }

    [Fact]
    public void Within_band_book_proposes_nothing_and_says_so()
    {
        var drift = Drift(
            Sleeve("Equity", 60, 55, 65, 61, "Within"),
            Sleeve("Bonds", 40, 35, 45, 39, "Within"));

        var proposal = PolicyReviewProposer.Propose(new InvestmentPolicyStatement(), drift, OnTime);

        proposal.Adjustments.Should().BeEmpty();
        proposal.Rationale.Should().Contain("within their bands");
    }

    [Fact]
    public void Rationale_summarises_the_adjustments_and_is_explicitly_recommend_only()
    {
        var drift = Drift(
            Sleeve("Equity", 60, 55, 65, 70, "OverBand"),
            Sleeve("Bonds", 40, 35, 45, 30, "UnderBand"));

        var proposal = PolicyReviewProposer.Propose(new InvestmentPolicyStatement { Version = 3 }, drift, OnTime);

        proposal.Rationale.Should().Contain("v3")
            .And.Contain("2 adjustment(s)")
            .And.Contain("Trim Equity")
            .And.Contain("Add Bonds")
            .And.EndWith(PolicyReviewProposer.RecommendOnlyNote);
    }

    [Fact]
    public void Rationale_notes_a_missed_review_and_an_assumed_cadence()
    {
        var late = OnTime with { IsMissed = true, DaysOverdue = 9, CadenceRecognised = false, Cadence = "sometimes" };

        var proposal = PolicyReviewProposer.Propose(new InvestmentPolicyStatement(), Drift(), late);

        proposal.Rationale.Should().Contain("9 day(s) after it was due")
            .And.Contain("\"sometimes\" is not recognised");
    }

    private static AllocationSleeveDrift Sleeve(
        string assetClass, decimal target, decimal min, decimal max, decimal actual, string status)
        => new(assetClass, target, min, max, actual, actual / 100m * TotalUsd, actual - target, status);

    private static AllocationDriftDto Drift(params AllocationSleeveDrift[] sleeves)
        => new(true, sleeves.Length == 0 ? 0 : TotalUsd, 0, TotalUsd, sleeves.Any(s => s.Status != "Within"), sleeves, "annual");
}
