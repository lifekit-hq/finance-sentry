namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Options;

public record GetTrackRecordQuery(Guid UserId, string? Source, string? Status) : IQuery<TrackRecordSummaryDto>;

/// <summary>
/// One-call "is this earning money" answer (US3, FR-007): counts, hit rate (terminal vs active,
/// never blended per R4), avg/median/best/worst excess return, split by source and status, with a
/// low-sample caveat below <see cref="MinimumClosedSampleSize"/> closed records. Candidate
/// (<c>Scan</c>) subjects don't exist yet (019) — the <c>BySource["Scan"]</c> slice is always
/// empty until then.
/// </summary>
public class GetTrackRecordQueryHandler(
    IThesisRepository thesisRepo,
    IThesisEventRepository eventRepo,
    IMarketDataService marketData,
    IThesisPerformanceCalculator calculator,
    IOptions<FrictionConfig> frictionOptions)
    : IQueryHandler<GetTrackRecordQuery, TrackRecordSummaryDto>
{
    public const int MinimumClosedSampleSize = 30;

    private const string SourceUser = "User";
    private const string SourceScan = "Scan";

    private const string StatusActive = "Active";
    private const string StatusBroken = "Broken";
    private const string StatusClosed = "Closed";

    public async Task<TrackRecordSummaryDto> Handle(GetTrackRecordQuery query, CancellationToken ct)
    {
        var theses = await thesisRepo.ListUnscopedAsync(query.UserId, ct);
        var allEvents = await eventRepo.ListUnscopedAsync(query.UserId, subjectId: null, ct);
        var eventsBySubject = allEvents
            .GroupBy(e => e.SubjectId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var records = new List<SubjectRecord>();

        var tickers = theses
            .Select(t => t.Ticker)
            .Append(ThesisEventRecorder.DefaultBenchmarkTicker)
            .Distinct()
            .ToArray();
        var liveQuotes = await marketData.GetQuotesAsync(tickers, ct);

        foreach (var thesis in theses)
        {
            if (!eventsBySubject.TryGetValue(thesis.Id, out var subjectEvents))
            {
                continue;
            }

            var created = subjectEvents.FirstOrDefault(e => e.EventType == ThesisEventType.Created);
            if (created is null)
            {
                continue;
            }

            var closed = subjectEvents.FirstOrDefault(e => e.EventType == ThesisEventType.Closed);

            // FR-006: a break is a TERMINAL outcome measured at the break event's stamped prices —
            // a broken thesis must not keep floating in the active bucket at live quotes.
            var broken = thesis.BrokenAt is not null
                ? subjectEvents
                    .Where(e => e.EventType == ThesisEventType.Broken)
                    .OrderByDescending(e => e.Timestamp)
                    .FirstOrDefault()
                : null;

            var status = closed is not null ? StatusClosed : broken is not null ? StatusBroken : StatusActive;

            var input = closed is not null
                ? BuildInput(thesis, created, ThesisEventType.Closed, closed.Timestamp, closed.SubjectPrice, closed.BenchmarkPrice)
                : broken is not null
                    ? BuildInput(thesis, created, ThesisEventType.Broken, broken.Timestamp, broken.SubjectPrice, broken.BenchmarkPrice)
                    : BuildLiveInput(thesis, created, liveQuotes);

            var result = calculator.Calculate(input);

            var isTerminal = closed is not null || broken is not null;
            records.Add(new SubjectRecord(SourceUser, status, isTerminal, result));
        }

        var filtered = records
            .Where(r => query.Source is null || string.Equals(r.Source, query.Source, StringComparison.OrdinalIgnoreCase))
            .Where(r => query.Status is null || string.Equals(r.Status, query.Status, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Summarize(filtered);
    }

    private ThesisPerformanceInput BuildInput(
        InvestmentThesis thesis,
        ThesisEvent created,
        ThesisEventType toEvent,
        DateTimeOffset toTimestamp,
        decimal? toSubjectPrice,
        decimal? toBenchmarkPrice)
        => new(
            thesis.Id,
            ThesisEventType.Created,
            created.Timestamp,
            created.SubjectPrice,
            created.BenchmarkPrice,
            toEvent,
            toTimestamp,
            toSubjectPrice,
            toBenchmarkPrice,
            thesis.Ticker,
            frictionOptions.Value);

    private ThesisPerformanceInput BuildLiveInput(
        InvestmentThesis thesis,
        ThesisEvent created,
        IReadOnlyDictionary<string, QuoteCacheEntry> liveQuotes)
    {
        var hasSubject = liveQuotes.TryGetValue(thesis.Ticker, out var subjectQuote);
        var hasBenchmark = liveQuotes.TryGetValue(ThesisEventRecorder.DefaultBenchmarkTicker, out var benchmarkQuote);

        return BuildInput(
            thesis,
            created,
            ThesisEventType.Snapshot,
            DateTimeOffset.UtcNow,
            hasSubject ? subjectQuote!.Price : null,
            hasBenchmark ? benchmarkQuote!.Price : null);
    }

    private static TrackRecordSummaryDto Summarize(IReadOnlyList<SubjectRecord> records)
    {
        var evaluable = records.Where(r => r.Result.IsEvaluable).ToList();
        var excludedCount = records.Count - evaluable.Count;

        var closed = evaluable.Where(r => r.IsTerminal).ToList();
        var active = evaluable.Where(r => !r.IsTerminal).ToList();

        var excessReturns = evaluable.Select(r => r.Result.ExcessReturnPct!.Value).ToList();

        return new TrackRecordSummaryDto(
            TotalCount: records.Count,
            ClosedCount: closed.Count,
            ExcludedCount: excludedCount,
            TerminalHitRate: HitRate(closed),
            ActiveHitRate: HitRate(active),
            AverageExcessReturnPct: Average(excessReturns),
            MedianExcessReturnPct: Median(excessReturns),
            BestExcessReturnPct: excessReturns.Count > 0 ? excessReturns.Max() : null,
            WorstExcessReturnPct: excessReturns.Count > 0 ? excessReturns.Min() : null,
            BySource: Slice(evaluable, r => r.Source, [SourceUser, SourceScan]),
            ByStatus: Slice(evaluable, r => r.Status, [StatusActive, StatusBroken, StatusClosed]),
            LowSampleCaveat: closed.Count < MinimumClosedSampleSize);
    }

    private static decimal? HitRate(IReadOnlyList<SubjectRecord> records)
    {
        if (records.Count == 0)
        {
            return null;
        }

        var hits = records.Count(r => r.Result.ExcessReturnPct is > 0m);
        return (decimal)hits / records.Count * 100m;
    }

    private static decimal? Average(IReadOnlyList<decimal> values)
        => values.Count > 0 ? values.Average() : null;

    private static decimal? Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2m : sorted[mid];
    }

    private static Dictionary<string, TrackRecordSliceDto> Slice(
        IReadOnlyList<SubjectRecord> records, Func<SubjectRecord, string> keySelector, string[] keys)
    {
        var result = new Dictionary<string, TrackRecordSliceDto>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var subset = records.Where(r => keySelector(r) == key).ToList();
            var excessReturns = subset.Select(r => r.Result.ExcessReturnPct!.Value).ToList();
            result[key] = new TrackRecordSliceDto(subset.Count, HitRate(subset), Average(excessReturns));
        }

        return result;
    }

    private sealed record SubjectRecord(string Source, string Status, bool IsTerminal, ThesisPerformanceResult Result);
}
