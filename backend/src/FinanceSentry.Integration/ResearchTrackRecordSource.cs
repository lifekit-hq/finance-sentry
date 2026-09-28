namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Feature 414 (US4) — implements the Radar module's <see cref="ITrackRecordSource"/> by reading
/// Research's published <see cref="ITrackRecordReader"/> port (#673). Lives in the Integration layer so Modules.Radar
/// never references Modules.Research directly (the 039/043 port precedent).
/// </summary>
public sealed class ResearchTrackRecordSource(
    ITrackRecordReader trackRecord) : ITrackRecordSource
{
    private const string StatusActive = "Active";
    private const string StatusBroken = "Broken";
    private const string StatusClosed = "Closed";

    public async Task<TrackRecordDelta?> GetDeltaAsync(Guid userId, CancellationToken ct = default)
    {
        var summary = await trackRecord.GetAsync(userId, ct);

        // Terminal records are the honest scorecard; fall back to open ones only when nothing has
        // closed yet. The two are never averaged together (feature 020 R4).
        if (summary.ClosedCount > 0)
        {
            return new TrackRecordDelta(
                IsTerminal: true,
                Count: summary.ClosedCount,
                HitRatePct: Rounded(summary.TerminalHitRate),
                AverageExcessReturnPct: Rounded(WeightedAverageExcess(summary, [StatusClosed, StatusBroken])),
                LowSample: summary.LowSampleCaveat);
        }

        var activeCount = Slice(summary, StatusActive)?.Count ?? 0;
        if (activeCount == 0)
        {
            return null;
        }

        return new TrackRecordDelta(
            IsTerminal: false,
            Count: activeCount,
            HitRatePct: Rounded(summary.ActiveHitRate),
            AverageExcessReturnPct: Rounded(Slice(summary, StatusActive)?.AverageExcessReturnPct),
            LowSample: summary.LowSampleCaveat);
    }

    /// <summary>Count-weighted mean across status slices — the top-level average blends terminal with active.</summary>
    private static decimal? WeightedAverageExcess(TrackRecordReading summary, string[] statuses)
    {
        var weighted = 0m;
        var total = 0;

        foreach (var status in statuses)
        {
            var slice = Slice(summary, status);
            if (slice is null || slice.Count == 0 || slice.AverageExcessReturnPct is null)
            {
                continue;
            }

            weighted += slice.AverageExcessReturnPct.Value * slice.Count;
            total += slice.Count;
        }

        return total > 0 ? weighted / total : null;
    }

    private static TrackRecordSliceReading? Slice(TrackRecordReading summary, string status)
        => summary.ByStatus.GetValueOrDefault(status);

    private static decimal? Rounded(decimal? value)
        => value.HasValue ? Math.Round(value.Value, 1) : null;
}
