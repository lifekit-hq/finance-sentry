namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public interface IBenchmarkTrackRecordMaterializer
{
    /// <summary>Computes and stores one run of the user's benchmark-relative track record.</summary>
    Task MaterializeAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Materializes the benchmark-relative track record (fs-699) from the persisted paired
/// <see cref="ThesisEvent"/> series — no quotes are fetched here, only stored prices are read. Every
/// thesis with a series counts, closed or deleted ones included, so the record carries no
/// survivorship bias. Each thesis is placed in the asset-class sleeve of its held position (or <see cref="BenchmarkRelativeCalculator.UnheldSleeve"/>),
/// and its net-of-friction figure is released only when the #688 reconciliation marks that brokerage
/// position's cost basis Verified. Book and sleeve rows in sustained underperformance raise an alert;
/// rows that recover resolve it.
/// </summary>
public sealed class BenchmarkTrackRecordMaterializer(
    IThesisEventRepository eventRepo,
    IBenchmarkRelativeRecordRepository recordRepo,
    IBookFiguresService bookFigures,
    IBrokerageHoldingsReader brokerage,
    IAlertGeneratorService alerts,
    BenchmarkRelativeCalculator calculator,
    IOptions<FrictionConfig> frictionOptions,
    IOptions<RelativePerformanceConfig> ruleOptions,
    TimeProvider clock,
    ILogger<BenchmarkTrackRecordMaterializer> logger) : IBenchmarkTrackRecordMaterializer
{
    private const string VerifiedBasisState = "Verified";

    /// <summary><see cref="BookFigures.StaleSources"/> entries that feed sleeve placement.</summary>
    private static readonly string[] HoldingSources = ["brokerage", "crypto"];

    public async Task MaterializeAsync(Guid userId, CancellationToken ct = default)
    {
        var series = (await eventRepo.ListAsync(userId, subjectId: null, ct))
            .Where(e => e.SubjectType == ThesisSubjectType.Thesis)
            .GroupBy(e => e.SubjectId)
            .ToList();
        if (series.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var asOf = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var rule = ruleOptions.Value;

        var figures = await bookFigures.ReadAsync(userId, ct);
        if (figures.StaleSources.Any(s => HoldingSources.Contains(s)))
        {
            // Sleeves come from holdings: a partial read would file held theses under Unheld and
            // resolve alerts that still stand. Keep the previous run until the next clean read.
            logger.LogWarning(
                "Benchmark track record: holdings stale ({Sources}) for user {UserId}; run skipped",
                string.Join(",", figures.StaleSources), userId);
            return;
        }

        var verifiedSymbols = await ReadVerifiedSymbolsAsync(userId, ct);

        var subjects = series
            .Select(g => ToSubject(g.Key, g.ToList(), figures.Positions, verifiedSymbols))
            .ToList();

        var rows = calculator.Compute(
            userId, asOf, now, ThesisEventRecorder.DefaultBenchmarkTicker, subjects, frictionOptions.Value);

        var previousRun = await recordRepo.ListPreviousRunAsync(userId, asOf, ct);
        BenchmarkRelativeCalculator.ApplyUnderperformance(rows, previousRun, rule);

        await recordRepo.ReplaceRunAsync(userId, asOf, rows, ct);
        await RaiseOrResolveAlertsAsync(userId, rows, previousRun, rule, ct);
    }

    private static TrackRecordSubject ToSubject(
        Guid thesisId,
        IReadOnlyList<ThesisEvent> events,
        IReadOnlyList<BookFigurePosition> positions,
        IReadOnlySet<string> verifiedSymbols)
    {
        var ticker = events.MaxBy(e => e.Timestamp)!.Ticker;
        var points = events
            .Where(e => !e.PricesPending && e.SubjectPrice is > 0m && e.BenchmarkPrice is > 0m)
            .Where(e => string.Equals(
                e.BenchmarkTicker, ThesisEventRecorder.DefaultBenchmarkTicker, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Timestamp)
            .Select(e => new TrackRecordPricePoint(e.EventType, e.Timestamp, e.SubjectPrice!.Value, e.BenchmarkPrice!.Value))
            .ToList();

        var held = positions
            .Where(p => string.Equals(p.Symbol, ticker, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.UsdValue)
            .FirstOrDefault();

        var gate = verifiedSymbols.Contains(ticker)
            ? NetExcessGate.Verified
            : held is null ? NetExcessGate.NotHeld : NetExcessGate.Unverified;

        return new TrackRecordSubject(
            thesisId,
            ticker,
            held?.AssetClass ?? BenchmarkRelativeCalculator.UnheldSleeve,
            gate,
            points);
    }

    /// <summary>
    /// Brokerage symbols whose cost basis the #688 reconciliation verified. A failed read verifies
    /// nothing — the net figure then stays withheld rather than trusting an unchecked basis.
    /// </summary>
    private async Task<IReadOnlySet<string>> ReadVerifiedSymbolsAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var holdings = await brokerage.GetHoldingsAsync(userId, ct);
            return holdings
                .Where(h => h.BasisState == VerifiedBasisState && h.Quantity > 0m)
                .Select(h => h.Symbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Benchmark track record: brokerage holdings read failed for user {UserId}", userId);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task RaiseOrResolveAlertsAsync(
        Guid userId,
        IReadOnlyList<BenchmarkRelativeRecord> rows,
        IReadOnlyList<BenchmarkRelativeRecord> previousRun,
        RelativePerformanceConfig rule,
        CancellationToken ct)
    {
        var alerting = rows.Where(r => IsAlerting(r, rule)).ToList();

        foreach (var row in alerting)
        {
            if (row.SustainedUnderperformance)
            {
                await alerts.GenerateRelativeUnderperformanceAlertAsync(
                    userId, row.Scope.ToString(), row.ScopeKey, row.Label, row.Window.ToString(), row.BenchmarkTicker,
                    row.ExcessReturnPct!.Value, row.UnderperformingRuns, rule.ThresholdPct, ct);
            }
            else
            {
                await alerts.ResolveRelativeUnderperformanceAlertAsync(userId, row.Scope.ToString(), row.ScopeKey, ct);
            }
        }

        // A sleeve that emptied out since the last run has no row left to recover through.
        var current = alerting.Select(r => (r.Scope, r.ScopeKey)).ToHashSet();
        foreach (var gone in previousRun.Where(r => IsAlerting(r, rule) && !current.Contains((r.Scope, r.ScopeKey))))
        {
            await alerts.ResolveRelativeUnderperformanceAlertAsync(userId, gone.Scope.ToString(), gone.ScopeKey, ct);
        }
    }

    private static bool IsAlerting(BenchmarkRelativeRecord row, RelativePerformanceConfig rule)
        => row.Window == rule.Window && row.Scope is TrackRecordScope.Book or TrackRecordScope.Sleeve;
}
