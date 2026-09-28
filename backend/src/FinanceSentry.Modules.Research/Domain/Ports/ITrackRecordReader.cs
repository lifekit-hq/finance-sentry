namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (#673): a user's thesis track record across every source and status, for
/// cross-module readers such as Radar's weekly performance brief. Implemented inside Research; the
/// FinanceSentry.Integration adapter reaches Research only through this interface.
/// </summary>
public interface ITrackRecordReader
{
    /// <summary>The user's unfiltered track record summary.</summary>
    Task<TrackRecordReading> GetAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// The track record as a cross-module reader sees it. Terminal and active hit rates are never
/// blended; <c>ByStatus</c> is keyed by thesis status (Active / Broken / Closed).
/// </summary>
public sealed record TrackRecordReading(
    int ClosedCount,
    decimal? TerminalHitRate,
    decimal? ActiveHitRate,
    bool LowSampleCaveat,
    IReadOnlyDictionary<string, TrackRecordSliceReading> ByStatus);

/// <summary>One status slice of the track record.</summary>
public sealed record TrackRecordSliceReading(
    int Count,
    decimal? HitRate,
    decimal? AverageExcessReturnPct);
