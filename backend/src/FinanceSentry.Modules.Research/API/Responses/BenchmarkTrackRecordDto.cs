namespace FinanceSentry.Modules.Research.API.Responses;

/// <summary>
/// The stored benchmark-relative track record (fs-699): the latest materialized run, read as-is.
/// <see cref="Note"/> explains an empty or filtered-out result instead of leaving it blank.
/// <see cref="Horizon"/> names the pinned measurement horizon the figures are judged on.
/// </summary>
public record BenchmarkTrackRecordDto(
    DateTimeOffset? AsOf,
    DateTimeOffset? ComputedAt,
    string BenchmarkTicker,
    UnderperformanceRuleDto Rule,
    IReadOnlyList<BenchmarkRelativeRowDto> Rows,
    string? Note,
    BenchmarkHorizonDto? Horizon = null);

/// <summary>
/// The pinned horizon (rolling 12 months plus since inception): <see cref="HeadlineWindows"/> are the
/// windows to cite for "are we beating the S&amp;P 500"; the other windows are context only.
/// </summary>
public record BenchmarkHorizonDto(IReadOnlyList<string> HeadlineWindows, string Note);

/// <summary>
/// One (scope, window) figure. Returns are percentages (5.0 = 5%). <see cref="NetExcessReturnPct"/>
/// is null unless <see cref="NetGate"/> is <c>Verified</c> — it nets friction off an acquisition
/// basis only once that basis is reconciled. <see cref="Headline"/> marks the two windows of the
/// pinned horizon (1Y and SinceInception).
/// </summary>
public record BenchmarkRelativeRowDto(
    string Scope,
    string ScopeKey,
    string Label,
    Guid? ThesisId,
    string Window,
    bool Covered,
    int ConstituentCount,
    DateTimeOffset? FromTimestamp,
    DateTimeOffset? ToTimestamp,
    decimal? SubjectReturnPct,
    decimal? BenchmarkReturnPct,
    decimal? ExcessReturnPct,
    decimal? NetExcessReturnPct,
    string NetGate,
    int UnderperformingRuns,
    bool SustainedUnderperformance,
    bool Headline = false);

/// <summary>The sustained-underperformance rule the stored flags were computed under.</summary>
public record UnderperformanceRuleDto(
    decimal ThresholdPct,
    int SustainedRuns,
    string Window,
    string Cadence);
