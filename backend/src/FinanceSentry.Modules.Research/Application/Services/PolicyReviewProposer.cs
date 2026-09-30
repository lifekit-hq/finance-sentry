namespace FinanceSentry.Modules.Research.Application.Services;

using System.Globalization;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;

/// <summary>
/// Turns allocation drift into the structured proposal a policy review records (#696): each sleeve's
/// drift against its band, the adjustment that returns an out-of-band sleeve to its target, and the
/// rationale for both. Targets and bands come from the policy statement through the drift figures,
/// never from anywhere else. Pure and recommend-only — it describes what the operator could do and
/// has no path to an order.
/// </summary>
public static class PolicyReviewProposer
{
    public const string RecommendOnlyNote =
        "Recommendation only: nothing has been placed, staged or routed. Act on it yourself if you agree.";

    private const decimal MaterialUnplannedPct = 1m;

    private const string StatusOverBand = "OverBand";
    private const string StatusUnderBand = "UnderBand";
    private const string StatusUnplanned = "Unplanned";

    public static PolicyReviewProposal Propose(
        InvestmentPolicyStatement ips, AllocationDriftDto drift, PolicyReviewStatus schedule)
    {
        var sleeves = drift.Sleeves
            .Select(s => new PolicyReviewSleeve(
                s.AssetClass, s.TargetPct, s.MinPct, s.MaxPct, s.ActualPct, s.ActualValueUsd, s.DriftPct, s.Status))
            .ToList();

        var adjustments = new List<PolicyReviewAdjustment>();
        foreach (var sleeve in drift.Sleeves)
        {
            var adjustment = AdjustmentFor(sleeve, drift.TotalValueUsd, ips.RebalancingRule.ContributionsFirst);
            if (adjustment is not null)
                adjustments.Add(adjustment);
        }

        return new PolicyReviewProposal(sleeves, adjustments, Rationale(ips, drift, schedule, adjustments));
    }

    private static PolicyReviewAdjustment? AdjustmentFor(AllocationSleeveDrift sleeve, decimal totalUsd, bool contributionsFirst)
    {
        var targetUsd = sleeve.TargetPct / 100m * totalUsd;
        var position = $"{sleeve.AssetClass} is {Pct(sleeve.ActualPct)} of the book against a {Pct(sleeve.TargetPct)} target "
            + $"(band {Pct(sleeve.MinPct)}–{Pct(sleeve.MaxPct)})";

        switch (sleeve.Status)
        {
            case StatusOverBand when sleeve.ActualValueUsd > targetUsd:
            {
                var amount = Math.Round(sleeve.ActualValueUsd - targetUsd, 2);
                return new PolicyReviewAdjustment(
                    sleeve.AssetClass, PolicyReviewAdjustmentAction.Trim, amount,
                    $"{position}, above its band. Trimming ≈ {Usd(amount)} returns it to target.");
            }
            case StatusUnderBand when targetUsd > sleeve.ActualValueUsd:
            {
                var amount = Math.Round(targetUsd - sleeve.ActualValueUsd, 2);
                var funding = contributionsFirst
                    ? " The policy directs new contributions to underweight sleeves first, so fund it from contributions before selling elsewhere."
                    : string.Empty;
                return new PolicyReviewAdjustment(
                    sleeve.AssetClass, PolicyReviewAdjustmentAction.Add, amount,
                    $"{position}, below its band. Adding ≈ {Usd(amount)} returns it to target.{funding}");
            }
            case StatusUnplanned when sleeve.ActualPct >= MaterialUnplannedPct:
                return new PolicyReviewAdjustment(
                    sleeve.AssetClass, PolicyReviewAdjustmentAction.Review, Math.Round(sleeve.ActualValueUsd, 2),
                    $"{sleeve.AssetClass} is {Pct(sleeve.ActualPct)} of the book but has no target in the policy statement. "
                    + "Decide whether the policy should carry it or the holding should wind down.");
            default:
                return null;
        }
    }

    private static string Rationale(
        InvestmentPolicyStatement ips, AllocationDriftDto drift, PolicyReviewStatus schedule,
        IReadOnlyList<PolicyReviewAdjustment> adjustments)
    {
        var parts = new List<string>
        {
            $"Scheduled {schedule.Cadence} review of policy statement v{ips.Version} on a {Usd(drift.TotalValueUsd)} book.",
        };

        if (!schedule.CadenceRecognised)
            parts.Add($"The recorded cadence \"{schedule.Cadence}\" is not recognised, so an annual cadence was assumed.");

        if (schedule.IsMissed)
            parts.Add($"This review opened {schedule.DaysOverdue} day(s) after it was due, so the scheduled review was missed.");

        if (drift.TotalValueUsd <= 0)
            parts.Add("No positions were valued, so there is nothing to measure against the bands.");
        else if (adjustments.Count == 0)
            parts.Add($"All {drift.Sleeves.Count(s => s.TargetPct > 0)} policy sleeve(s) are within their bands; no adjustment is proposed.");
        else
            parts.Add($"{adjustments.Count} adjustment(s) proposed: "
                + string.Join("; ", adjustments.Select(a => $"{a.Action} {a.AssetClass} ≈ {Usd(a.ApproxAmountUsd)}")) + ".");

        parts.Add(RecommendOnlyNote);
        return string.Join(" ", parts);
    }

    private static string Pct(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string Usd(decimal value) => "$" + value.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>The structured output of a policy review: drift against bands, adjustments, rationale.</summary>
public sealed record PolicyReviewProposal(
    IReadOnlyList<PolicyReviewSleeve> Sleeves,
    IReadOnlyList<PolicyReviewAdjustment> Adjustments,
    string Rationale);
