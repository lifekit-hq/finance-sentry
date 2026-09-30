namespace FinanceSentry.Modules.Research.Domain.PolicyReviews;

/// <summary>
/// Whether the user's risk tolerance, capacity and drawdown tolerance are due to be re-measured
/// (#700). They ride the policy review cadence (#696) — the same cadence months recorded on the
/// statement — rather than a second schedule: a measurement older than one cadence, or one that
/// never happened, is due, and so is a statement that still has no drawdown tolerance (the figure
/// the risk layer enforces), however recent the rest of the measurement is.
/// </summary>
public static class RiskMeasurementSchedule
{
    public static RiskMeasurementStatus Evaluate(InvestmentPolicyStatement current, DateTimeOffset now)
    {
        PolicyReviewCadence.TryGetMonths(current.ReviewCadence, out var months);

        var drawdownSet = current.MaxDrawdownTolerancePct is > 0;
        var measuredAt = current.RiskMeasuredAt;
        var lapsed = measuredAt is null || now >= measuredAt.Value.AddMonths(months);

        return new RiskMeasurementStatus(measuredAt, drawdownSet, !drawdownSet || lapsed);
    }

    /// <summary>The request the review raises for a due re-measurement, or null when none is due.</summary>
    public static string? RequestText(RiskMeasurementStatus status, string cadence)
    {
        if (!status.IsDue)
            return null;

        var reason = !status.DrawdownToleranceSet
            ? "No drawdown tolerance is recorded, so the risk layer has no decline threshold to enforce."
            : status.MeasuredAt is null
                ? "Risk tolerance and capacity have never been measured."
                : $"Risk tolerance and capacity were last measured {status.MeasuredAt:yyyy-MM-dd}, more than one {cadence} cycle ago.";

        return $"Re-measure risk on this review: {reason} Ask the owner how large a decline they would sit through "
            + "(tolerance, capacity, maximum drawdown) and record the answer; it becomes a new policy version.";
    }
}

/// <summary>When risk was last measured, whether a drawdown tolerance is recorded, and whether a re-measurement is due.</summary>
public sealed record RiskMeasurementStatus(DateTimeOffset? MeasuredAt, bool DrawdownToleranceSet, bool IsDue);
