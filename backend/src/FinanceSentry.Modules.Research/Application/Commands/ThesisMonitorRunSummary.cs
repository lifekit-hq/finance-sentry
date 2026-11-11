namespace FinanceSentry.Modules.Research.Application.Commands;

using FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// Aggregate outcome of one thesis-monitor run (FR-016) — returned in-band from
/// <see cref="RunThesisMonitorCommand"/> and logged by <c>ThesisMonitorJob</c>.
/// <see cref="Unmonitorable"/> (#837) names every thesis with at least one trigger the run could not
/// evaluate, and why: a thesis whose triggers have no data is blind, never silently "intact".
/// <see cref="CoverageDegraded"/> (#1375) names every trigger that evaluated on the previous run and could not on
/// this one; an alert has already been raised for each.
/// </summary>
public record ThesisMonitorRunSummary(
    int ThesesEvaluated,
    int TriggersEvaluated,
    int BreaksRaised,
    int BreaksCleared,
    int Skipped,
    int Errors,
    IReadOnlyList<UnmonitorableThesis> Unmonitorable,
    IReadOnlyList<CoverageDegradation>? CoverageDegraded = null);

/// <summary>
/// A thesis whose monitoring coverage regressed since the previous run (#1375): the listed triggers evaluated then
/// and are blind now (<see cref="Since"/> is this run's time, the first run they failed).
/// </summary>
public record CoverageDegradation(
    Guid ThesisId,
    string Ticker,
    DateTimeOffset Since,
    IReadOnlyList<UnmonitorableTrigger> Triggers);

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
/// which basis, or why none could). <see cref="Cause"/> (#1375) is <see cref="ThesisBlindCause.InfraBlind"/>
/// when a data source failed (the trigger is unwatched because infrastructure is down, and retrying later
/// can fix it) and <see cref="ThesisBlindCause.NoData"/> when the data simply is not there.
/// </summary>
public record UnmonitorableTrigger(
    string Metric,
    string? PeriodType,
    string Subject,
    string Reason,
    FundamentalsCoverage? Coverage,
    string Cause = ThesisBlindCause.NoData);

public static class ThesisBlindCause
{
    /// <summary>A data source failed (provider outage, ticker→CIK map down): not a property of the thesis.</summary>
    public const string InfraBlind = "INFRA_BLIND";

    /// <summary>No data to evaluate against (a thin history, a filer with no facts, an unsupported metric).</summary>
    public const string NoData = "NO_DATA";
}

public static class ThesisMonitorability
{
    public const string Unmonitorable = "UNMONITORABLE";
    public const string PartiallyMonitorable = "PARTIALLY_MONITORABLE";
}
