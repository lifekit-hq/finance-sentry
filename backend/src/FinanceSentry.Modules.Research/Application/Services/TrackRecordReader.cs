namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary><see cref="ITrackRecordReader"/> impl over Research's own <see cref="GetTrackRecordQuery"/> (no source or status filter).</summary>
public sealed class TrackRecordReader(
    IQueryHandler<GetTrackRecordQuery, TrackRecordSummaryDto> trackRecord) : ITrackRecordReader
{
    public async Task<TrackRecordReading> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var summary = await trackRecord.Handle(new GetTrackRecordQuery(userId, Source: null, Status: null), ct);
        var byStatus = summary.ByStatus.ToDictionary(
            kv => kv.Key,
            kv => new TrackRecordSliceReading(kv.Value.Count, kv.Value.HitRate, kv.Value.AverageExcessReturnPct));
        return new TrackRecordReading(
            summary.ClosedCount, summary.TerminalHitRate, summary.ActiveHitRate, summary.LowSampleCaveat, byStatus);
    }
}
