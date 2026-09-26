namespace FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// Structural evaluability of a trigger — derivable from its own fields, no I/O. A trigger that
/// fails this check can never be evaluated regardless of data availability (unsupported metric,
/// missing/invalid direction, non-positive consecutive-periods window), and is rejected at save
/// time (<see cref="Application.Validation.ThesisTriggerVocabulary"/>). The same check is reused to
/// flag legacy rows that predate that gate, so they don't silently claim coverage they don't have.
/// </summary>
public static class ThesisTriggerEvaluability
{
    public static bool IsStructurallyEvaluable(ThesisInvalidationTrigger trigger, out string? reason)
    {
        if (!ThesisMetric.Contains(trigger.Metric))
        {
            reason = NonEvaluableReason.UnsupportedMetric;
            return false;
        }

        if (!ThesisTriggerDirection.IsKnown(trigger.Direction))
        {
            reason = NonEvaluableReason.InvalidDirection;
            return false;
        }

        if (trigger.ConsecutivePeriods < 1)
        {
            reason = NonEvaluableReason.InvalidConsecutivePeriods;
            return false;
        }

        reason = null;
        return true;
    }
}
