namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// Aggregate outcome of one thesis-monitor run (FR-016) — returned in-band from
/// <see cref="RunThesisMonitorCommand"/> and logged by <c>ThesisMonitorJob</c>.
/// <see cref="Unmonitorable"/> (#837) names every thesis with at least one trigger the run could not
/// evaluate, and why: a thesis whose triggers have no data is blind, never silently "intact".
/// </summary>
public record ThesisMonitorRunSummary(
    int ThesesEvaluated,
    int TriggersEvaluated,
    int BreaksRaised,
    int BreaksCleared,
    int Skipped,
    int Errors,
    IReadOnlyList<UnmonitorableThesis> Unmonitorable);

/// <summary>
/// A thesis the run was blind on. <see cref="Status"/> is <see cref="ThesisMonitorability.Unmonitorable"/>
/// when no trigger could be evaluated, <see cref="ThesisMonitorability.PartiallyMonitorable"/> when only
/// the listed ones could not.
/// </summary>
public record UnmonitorableThesis(
    Guid ThesisId,
    string Ticker,
    string Status,
    IReadOnlyList<UnmonitorableTrigger> Triggers);

/// <summary>
/// One trigger the run could not evaluate: its metric and period basis, the ticker it reads
/// (<see cref="Subject"/> — the proxy when one is set), the non-evaluable reason, and for a
/// fundamentals metric the subject's coverage from the fundamentals chain (which provider supplied
/// which basis, or why none could).
/// </summary>
public record UnmonitorableTrigger(
    string Metric,
    string? PeriodType,
    string Subject,
    string Reason,
    FundamentalsCoverage? Coverage);

public static class ThesisMonitorability
{
    public const string Unmonitorable = "UNMONITORABLE";
    public const string PartiallyMonitorable = "PARTIALLY_MONITORABLE";
}
