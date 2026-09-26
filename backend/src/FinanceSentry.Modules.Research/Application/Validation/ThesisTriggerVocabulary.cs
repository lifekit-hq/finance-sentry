namespace FinanceSentry.Modules.Research.Application.Validation;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// Guards, at save time, that every invalidation trigger is one the monitor can actually run:
/// a metric from the closed <see cref="ThesisMetric"/> vocabulary (FR-012), with a known direction
/// and a positive consecutive-periods window, and no two triggers contradicting each other on the
/// same metric+direction (one would fire while the other stays healthy).
/// </summary>
public static class ThesisTriggerVocabulary
{
    public static void Validate(IReadOnlyList<ThesisInvalidationTrigger> triggers)
    {
        var seen = new HashSet<(string Metric, string Direction)>();

        foreach (var trigger in triggers)
        {
            if (!ThesisTriggerEvaluability.IsStructurallyEvaluable(trigger, out var reason))
            {
                throw reason == NonEvaluableReason.UnsupportedMetric
                    ? new InvalidThesisTriggerException(trigger.Metric)
                    : new InvalidThesisTriggerScaffoldingException(trigger.Metric, reason!);
            }

            if (!seen.Add((trigger.Metric, trigger.Direction)))
            {
                throw new ContradictoryThesisTriggerException(trigger.Metric, trigger.Direction);
            }
        }
    }
}
