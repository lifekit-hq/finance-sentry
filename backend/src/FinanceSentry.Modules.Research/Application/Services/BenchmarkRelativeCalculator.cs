namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;

/// <summary>One persisted, fully priced point of a thesis's paired subject/benchmark series.</summary>
public sealed record TrackRecordPricePoint(
    ThesisEventType EventType,
    DateTimeOffset Timestamp,
    decimal SubjectPrice,
    decimal BenchmarkPrice);

/// <summary>
/// A thesis as the benchmark-relative track record sees it: its priced series (ascending), the
/// sleeve it sits in, and the <see cref="NetExcessGate"/> state of its position's cost basis.
/// </summary>
public sealed record TrackRecordSubject(
    Guid ThesisId,
    string Ticker,
    string Sleeve,
    string NetGate,
    IReadOnlyList<TrackRecordPricePoint> Points);

/// <summary>
/// Pure benchmark-relative track-record math (fs-699) — no I/O. Turns each thesis's persisted
/// paired series into per-window thesis rows, then rolls them up equal-weighted into one book row
/// and one row per sleeve. Return and friction math is delegated to
/// <see cref="IThesisPerformanceCalculator"/> so this and the per-thesis performance tool agree to
/// the digit.
/// </summary>
public sealed class BenchmarkRelativeCalculator(IThesisPerformanceCalculator performance)
{
    public const string BookKey = "book";
    public const string BookLabel = "Book";

    /// <summary>Sleeve for a thesis whose ticker is not currently held.</summary>
    public const string UnheldSleeve = "Unheld";

    /// <summary>
    /// How far past a window's start the opening anchor may sit. Snapshots are weekly, so a strict
    /// "on or before the start" rule would stretch a one-month window to five weeks; half a cadence
    /// keeps each window within a few days of its nominal length.
    /// </summary>
    public static readonly TimeSpan AnchorGrace = TimeSpan.FromDays(3);

    private const int StoredDecimals = 4;

    public static IReadOnlyList<TrackRecordWindow> Windows { get; } =
    [
        TrackRecordWindow.OneMonth,
        TrackRecordWindow.ThreeMonths,
        TrackRecordWindow.OneYear,
        TrackRecordWindow.SinceInception,
    ];

    public IReadOnlyList<BenchmarkRelativeRecord> Compute(
        Guid userId,
        DateTimeOffset asOf,
        DateTimeOffset now,
        string benchmarkTicker,
        IReadOnlyList<TrackRecordSubject> subjects,
        FrictionConfig friction)
    {
        var rows = new List<BenchmarkRelativeRecord>();

        foreach (var window in Windows)
        {
            var results = subjects
                .Select(s => (Subject: s, Result: Measure(s, window, now, friction)))
                .ToList();

            foreach (var (subject, result) in results)
            {
                rows.Add(ThesisRow(userId, asOf, now, benchmarkTicker, window, subject, result));
            }

            rows.Add(AggregateRow(
                userId, asOf, now, benchmarkTicker, window, TrackRecordScope.Book, BookKey, BookLabel, results));

            foreach (var sleeve in results.GroupBy(r => r.Subject.Sleeve, StringComparer.Ordinal).OrderBy(g => g.Key))
            {
                rows.Add(AggregateRow(
                    userId, asOf, now, benchmarkTicker, window, TrackRecordScope.Sleeve, sleeve.Key, sleeve.Key,
                    sleeve.ToList()));
            }
        }

        return rows;
    }

    /// <summary>
    /// Stamps each row with how many consecutive runs (this one included) its excess sat at or below
    /// −<see cref="RelativePerformanceConfig.ThresholdPct"/>, chaining from the same (scope, key,
    /// window) row of the previous run, and flags sustained underperformance once that count reaches
    /// <see cref="RelativePerformanceConfig.SustainedRuns"/>. An uncovered window breaks the chain.
    /// </summary>
    public static void ApplyUnderperformance(
        IReadOnlyList<BenchmarkRelativeRecord> rows,
        IReadOnlyList<BenchmarkRelativeRecord> previousRun,
        RelativePerformanceConfig config)
    {
        var previousByKey = previousRun.ToDictionary(r => (r.Scope, r.ScopeKey, r.Window));

        foreach (var row in rows)
        {
            if (row.ExcessReturnPct is not { } excess || excess > -config.ThresholdPct)
            {
                row.UnderperformingRuns = 0;
                row.SustainedUnderperformance = false;
                continue;
            }

            var carried = previousByKey.TryGetValue((row.Scope, row.ScopeKey, row.Window), out var previous)
                ? previous.UnderperformingRuns
                : 0;

            row.UnderperformingRuns = carried + 1;
            row.SustainedUnderperformance = row.UnderperformingRuns >= config.SustainedRuns;
        }
    }

    private Measured? Measure(
        TrackRecordSubject subject, TrackRecordWindow window, DateTimeOffset now, FrictionConfig friction)
    {
        var points = subject.Points.Where(p => p.Timestamp <= now).OrderBy(p => p.Timestamp).ToList();
        if (points.Count < 2)
        {
            return null;
        }

        var to = points[^1];
        var from = window == TrackRecordWindow.SinceInception
            ? points[0]
            : points.LastOrDefault(p => p.Timestamp <= WindowStart(window, now) + AnchorGrace);

        if (from is null || from.Timestamp >= to.Timestamp)
        {
            return null;
        }

        var result = performance.Calculate(new ThesisPerformanceInput(
            subject.ThesisId,
            from.EventType,
            from.Timestamp,
            from.SubjectPrice,
            from.BenchmarkPrice,
            to.EventType,
            to.Timestamp,
            to.SubjectPrice,
            to.BenchmarkPrice,
            subject.Ticker,
            friction));

        return result.IsEvaluable ? new Measured(result, from.Timestamp, to.Timestamp) : null;
    }

    private static DateTimeOffset WindowStart(TrackRecordWindow window, DateTimeOffset now) => window switch
    {
        TrackRecordWindow.OneMonth => now.AddMonths(-1),
        TrackRecordWindow.ThreeMonths => now.AddMonths(-3),
        TrackRecordWindow.OneYear => now.AddYears(-1),
        _ => DateTimeOffset.MinValue,
    };

    private static BenchmarkRelativeRecord ThesisRow(
        Guid userId,
        DateTimeOffset asOf,
        DateTimeOffset now,
        string benchmarkTicker,
        TrackRecordWindow window,
        TrackRecordSubject subject,
        Measured? measured)
    {
        var result = measured?.Result;
        var verified = subject.NetGate == NetExcessGate.Verified;

        return new BenchmarkRelativeRecord
        {
            UserId = userId,
            AsOf = asOf,
            Scope = TrackRecordScope.Thesis,
            ScopeKey = subject.ThesisId.ToString("N"),
            Label = subject.Ticker,
            ThesisId = subject.ThesisId,
            Window = window,
            BenchmarkTicker = benchmarkTicker,
            Covered = result is not null,
            ConstituentCount = result is null ? 0 : 1,
            FromTimestamp = measured?.From,
            ToTimestamp = measured?.To,
            SubjectReturnPct = Round(result?.AbsoluteReturnPct),
            BenchmarkReturnPct = Round(result?.BenchmarkReturnPct),
            ExcessReturnPct = Round(result?.ExcessReturnPct),
            NetExcessReturnPct = verified ? Round(result?.NetExcessReturnPct) : null,
            NetGate = subject.NetGate,
            ComputedAt = now,
        };
    }

    private static BenchmarkRelativeRecord AggregateRow(
        Guid userId,
        DateTimeOffset asOf,
        DateTimeOffset now,
        string benchmarkTicker,
        TrackRecordWindow window,
        TrackRecordScope scope,
        string key,
        string label,
        IReadOnlyList<(TrackRecordSubject Subject, Measured? Result)> members)
    {
        var covered = members.Where(m => m.Result is not null).ToList();
        var gate = AggregateGate(covered.Count > 0 ? covered : members);

        return new BenchmarkRelativeRecord
        {
            UserId = userId,
            AsOf = asOf,
            Scope = scope,
            ScopeKey = key,
            Label = label,
            Window = window,
            BenchmarkTicker = benchmarkTicker,
            Covered = covered.Count > 0,
            ConstituentCount = covered.Count,
            FromTimestamp = covered.Count > 0 ? covered.Min(m => m.Result!.From) : null,
            ToTimestamp = covered.Count > 0 ? covered.Max(m => m.Result!.To) : null,
            SubjectReturnPct = Round(Mean(covered, r => r.AbsoluteReturnPct)),
            BenchmarkReturnPct = Round(Mean(covered, r => r.BenchmarkReturnPct)),
            ExcessReturnPct = Round(Mean(covered, r => r.ExcessReturnPct)),
            NetExcessReturnPct = gate == NetExcessGate.Verified ? Round(Mean(covered, r => r.NetExcessReturnPct)) : null,
            NetGate = gate,
            ComputedAt = now,
        };
    }

    /// <summary>Verified only when every counted constituent is; all-unheld stays NotHeld; any mix is Incomplete.</summary>
    private static string AggregateGate(
        IReadOnlyList<(TrackRecordSubject Subject, Measured? Result)> members)
    {
        if (members.Count == 0)
        {
            return NetExcessGate.Incomplete;
        }

        if (members.All(m => m.Subject.NetGate == NetExcessGate.Verified))
        {
            return NetExcessGate.Verified;
        }

        return members.All(m => m.Subject.NetGate == NetExcessGate.NotHeld)
            ? NetExcessGate.NotHeld
            : NetExcessGate.Incomplete;
    }

    private static decimal? Mean(
        IReadOnlyList<(TrackRecordSubject Subject, Measured? Result)> covered,
        Func<ThesisPerformanceResult, decimal?> selector)
    {
        var values = covered.Select(m => selector(m.Result!.Result)).OfType<decimal>().ToList();
        return values.Count == 0 ? null : values.Average();
    }

    private static decimal? Round(decimal? value)
        => value is { } v ? Math.Round(v, StoredDecimals, MidpointRounding.AwayFromZero) : null;

    private sealed record Measured(ThesisPerformanceResult Result, DateTimeOffset From, DateTimeOffset To);
}
