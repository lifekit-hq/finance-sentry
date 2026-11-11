namespace FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// What the thesis monitor could evaluate on its last run for one thesis (#1375): the trigger keys
/// that produced a verdict and how many triggers the thesis had. The next run diffs against it, so a
/// trigger that evaluated last run and fails now is a coverage regression the owner is told about,
/// not a quiet slide into <c>PARTIALLY_MONITORABLE</c>. Stored on the thesis row; the evaluable count
/// is <see cref="EvaluableKeys"/>.Count.
/// </summary>
public sealed record ThesisMonitorSnapshot(
    DateTimeOffset At,
    int TotalTriggers,
    IReadOnlyList<string> EvaluableKeys);
