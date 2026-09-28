namespace FinanceSentry.Tests.Integration.CrossModulePorts;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Integration;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FluentAssertions;
using Xunit;

/// <summary>
/// 414 (US4): the track-record port feeds the weekly brief. It must never blend terminal and
/// active records (feature 020 R4) — the top-level average in the summary DTO does exactly that,
/// so the adapter derives the number from the per-status slices instead.
/// #673: composed over Research's published <c>ITrackRecordReader</c> port.
/// </summary>
public sealed class ResearchTrackRecordSourceTests
{
    private sealed class StubTrackRecord(TrackRecordSummaryDto dto)
        : IQueryHandler<GetTrackRecordQuery, TrackRecordSummaryDto>
    {
        public Task<TrackRecordSummaryDto> Handle(GetTrackRecordQuery query, CancellationToken cancellationToken)
            => Task.FromResult(dto);
    }

    private static TrackRecordSummaryDto Summary(
        int closedCount,
        decimal? terminalHitRate,
        decimal? activeHitRate,
        TrackRecordSliceDto closed,
        TrackRecordSliceDto broken,
        TrackRecordSliceDto active,
        bool lowSample = false) =>
        new(
            TotalCount: closed.Count + broken.Count + active.Count,
            ClosedCount: closedCount,
            ExcludedCount: 0,
            TerminalHitRate: terminalHitRate,
            ActiveHitRate: activeHitRate,
            AverageExcessReturnPct: 99m, // blended — the adapter must not use it
            MedianExcessReturnPct: null,
            BestExcessReturnPct: null,
            WorstExcessReturnPct: null,
            BySource: new Dictionary<string, TrackRecordSliceDto>(),
            ByStatus: new Dictionary<string, TrackRecordSliceDto>
            {
                ["Closed"] = closed,
                ["Broken"] = broken,
                ["Active"] = active,
            },
            LowSampleCaveat: lowSample);

    private static ResearchTrackRecordSource Source(TrackRecordSummaryDto dto) => new(new TrackRecordReader(new StubTrackRecord(dto)));

    [Fact]
    public async Task ReportsTerminalRecords_WeightingClosedAndBrokenByCount()
    {
        // 6 closed at +5%, 2 broken at -3% → (6·5 + 2·-3) / 8 = +3.0%.
        var source = Source(Summary(
            closedCount: 8,
            terminalHitRate: 62.5m,
            activeHitRate: 100m,
            closed: new TrackRecordSliceDto(6, 66.7m, 5m),
            broken: new TrackRecordSliceDto(2, 50m, -3m),
            active: new TrackRecordSliceDto(4, 100m, 40m),
            lowSample: true));

        var delta = await source.GetDeltaAsync(Guid.NewGuid(), CancellationToken.None);

        delta.Should().NotBeNull();
        delta!.IsTerminal.Should().BeTrue();
        delta.Count.Should().Be(8);
        delta.HitRatePct.Should().Be(62.5m);
        delta.AverageExcessReturnPct.Should().Be(3.0m);
        delta.LowSample.Should().BeTrue();
    }

    [Fact]
    public async Task FallsBackToOpenRecords_WhenNothingHasClosedYet()
    {
        var source = Source(Summary(
            closedCount: 0,
            terminalHitRate: null,
            activeHitRate: 60m,
            closed: new TrackRecordSliceDto(0, null, null),
            broken: new TrackRecordSliceDto(0, null, null),
            active: new TrackRecordSliceDto(5, 60m, 1.44m),
            lowSample: true));

        var delta = await source.GetDeltaAsync(Guid.NewGuid(), CancellationToken.None);

        delta.Should().NotBeNull();
        delta!.IsTerminal.Should().BeFalse();
        delta.Count.Should().Be(5);
        delta.HitRatePct.Should().Be(60m);
        delta.AverageExcessReturnPct.Should().Be(1.4m); // rounded to one decimal for the brief
    }

    [Fact]
    public async Task ReturnsNull_WhenTheUserHasNoEvaluableRecordAtAll()
    {
        var source = Source(Summary(
            closedCount: 0,
            terminalHitRate: null,
            activeHitRate: null,
            closed: new TrackRecordSliceDto(0, null, null),
            broken: new TrackRecordSliceDto(0, null, null),
            active: new TrackRecordSliceDto(0, null, null)));

        var delta = await source.GetDeltaAsync(Guid.NewGuid(), CancellationToken.None);

        delta.Should().BeNull();
    }
}
