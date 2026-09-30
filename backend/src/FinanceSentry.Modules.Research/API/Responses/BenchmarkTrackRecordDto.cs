namespace FinanceSentry.Modules.Research.API.Responses;

/// <summary>
/// The stored benchmark-relative track record (fs-699): the latest materialized run, read as-is.
/// <see cref="Note"/> explains an empty or filtered-out result instead of leaving it blank.
/// </summary>
public record BenchmarkTrackRecordDto(
    DateTimeOffset? AsOf,
    DateTimeOffset? ComputedAt,
    string BenchmarkTicker,
    UnderperformanceRuleDto Rule,
    IReadOnlyList<BenchmarkRelativeRowDto> Rows,
    string? Note);

/// <summary>
/// One (scope, window) figure. Returns are percentages (5.0 = 5%). <see cref="NetExcessReturnPct"/>
/// is null unless <see cref="NetGate"/> is <c>Verified</c> — it nets friction off an acquisition
/// basis only once that basis is reconciled.
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
    bool SustainedUnderperformance);

/// <summary>The sustained-underperformance rule the stored flags were computed under.</summary>
public record UnderperformanceRuleDto(
    decimal ThresholdPct,
    int SustainedRuns,
    string Window,
    string Cadence);
