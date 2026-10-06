namespace FinanceSentry.Modules.Research.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using Hangfire;
using Microsoft.Extensions.Logging;

/// <summary>
/// Nightly analyst-actions ingestion (feature 030). Syncs the universe, then runs every registered
/// source with per-source failure isolation: one source failing never blocks the other, and each
/// source raises a sync-failure alert only after two consecutive failures (FR-009). Overlap-protected
/// via <see cref="DisableConcurrentExecutionAttribute"/> (spec overlap edge case).
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
public sealed class AnalystActionsIngestionJob(
    IAnalystUniverseService universeService,
    IEnumerable<IAnalystActionsSource> sources,
    IAnalystActionRepository repository,
    IAnalystSourceHealth health,
    IBankingTotalsReader banking,
    IAlertGeneratorService alerts,
    IValuationDataService valuation,
    IValuationSnapshotRepository valuationSnapshots,
    IRecommendationTrendsService recommendationTrends,
    IRecommendationTrendRepository recommendationTrendRepository,
    ILogger<AnalystActionsIngestionJob> logger)
{
    private const int AlertThreshold = 2;

    // Reasons for which we capture a nightly valuation snapshot (R9: the small owned/tracked set, not
    // the whole index seed) so each ticker's self-built comparison window grows.
    private static readonly UniverseReason[] ValuationCaptureReasons =
        [UniverseReason.Holding, UniverseReason.Watchlist, UniverseReason.Candidate, UniverseReason.Manual];

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var members = await universeService.SyncAsync(ct);
        var universe = members.Select(m => m.Ticker).ToArray();

        foreach (var source in sources)
        {
            await RunSourceAsync(source, universe, ct);
        }

        await CaptureValuationSnapshotsAsync(members, ct);
        await CaptureRecommendationTrendsAsync(members, ct);
    }

    // Structured monthly consensus for the tracked set (feature 037). Same isolation posture as the
    // valuation capture: a Finnhub failure never fails the actions run; total failures strike the
    // health counter under "finnhub" so the existing 2-strike alert path fires.
    private async Task CaptureRecommendationTrendsAsync(
        IReadOnlyList<AnalystUniverseMember> members, CancellationToken ct)
    {
        if (!recommendationTrends.IsConfigured)
        {
            logger.LogDebug("Recommendation trends capture skipped — no Finnhub API key configured");
            return;
        }

        var tickers = members
            .Where(m => ValuationCaptureReasons.Contains(m.Reason))
            .Select(m => m.Ticker)
            .Distinct()
            .ToArray();

        try
        {
            var trends = await recommendationTrends.FetchAsync(tickers, ct);
            var inserted = await recommendationTrendRepository.UpsertAsync(trends, ct);
            health.RecordSuccess(FinnhubRecommendationTrendsService.SourceName);
            logger.LogInformation(
                "Recommendation trends captured for {Covered}/{Total} tracked tickers ({Inserted} new months)",
                trends.Select(t => t.Ticker).Distinct().Count(), tickers.Length, inserted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var consecutive = health.RecordFailure(FinnhubRecommendationTrendsService.SourceName);
            logger.LogError(ex,
                "Recommendation trends capture failed ({Consecutive} consecutive)", consecutive);

            if (consecutive >= AlertThreshold)
            {
                await RaiseFailureAlertAsync(FinnhubRecommendationTrendsService.SourceName, ex.Message, ct);
            }
        }
    }

    // Persist a current-metrics valuation snapshot for each holdings/watchlist/candidate ticker so the
    // self-built history window grows (feature 030, R9). Per-ticker failure isolation: one ticker's
    // fetch failing never blocks the rest, and this capture never fails the analyst-actions run.
    private async Task CaptureValuationSnapshotsAsync(
        IReadOnlyList<AnalystUniverseMember> members, CancellationToken ct)
    {
        var tickers = members
            .Where(m => ValuationCaptureReasons.Contains(m.Reason))
            .Select(m => m.Ticker)
            .Distinct()
            .ToArray();

        var captured = 0;
        foreach (var ticker in tickers)
        {
            try
            {
                var metrics = await valuation.GetCurrentMetricsAsync(ticker, ct);
                if (metrics is null || metrics.NotApplicable)
                {
                    continue;
                }

                await valuationSnapshots.AddAsync(new ValuationSnapshot
                {
                    Ticker = ticker,
                    Price = metrics.Price ?? 0m,
                    TrailingPe = metrics.TrailingPe,
                    ForwardPe = metrics.ForwardPe,
                    EvToEbitda = metrics.EvToEbitda,
                    DividendYield = metrics.DividendYield,
                    ConsensusTarget = metrics.ConsensusTarget,
                    IsStale = metrics.IsStale,
                }, ct);
                captured++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Valuation snapshot capture failed for {Ticker}", ticker);
            }
        }

        logger.LogInformation("Valuation snapshots captured for {Captured}/{Total} tracked tickers",
            captured, tickers.Length);
    }

    private async Task RunSourceAsync(
        IAnalystActionsSource source, IReadOnlyCollection<string> universe, CancellationToken ct)
    {
        try
        {
            var records = await source.FetchAsync(universe, ct);
            var now = DateTimeOffset.UtcNow;
            var entities = records.Select(r => new AnalystAction
            {
                Ticker = r.Ticker,
                Firm = r.Firm,
                ActionType = r.ActionType,
                PriorRating = r.PriorRating,
                NewRating = r.NewRating,
                PriorTarget = r.PriorTarget,
                NewTarget = r.NewTarget,
                ActionDate = r.ActionDate,
                Source = source.SourceName,
                SourceUrl = r.SourceUrl,
                IngestedAt = now,
            }).ToList();

            var inserted = await repository.UpsertAsync(entities, ct);
            health.RecordSuccess(source.SourceName);
            logger.LogInformation(
                "Analyst source {Source}: {Fetched} fetched, {Inserted} new", source.SourceName, records.Count, inserted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var consecutive = health.RecordFailure(source.SourceName);
            logger.LogError(ex,
                "Analyst source {Source} failed ({Consecutive} consecutive)", source.SourceName, consecutive);

            if (consecutive >= AlertThreshold)
            {
                await RaiseFailureAlertAsync(source.SourceName, ex.Message, ct);
            }
        }
    }

    private async Task RaiseFailureAlertAsync(string source, string reason, CancellationToken ct)
    {
        var provider = $"analyst-actions:{source}";
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        foreach (var userId in userIds)
        {
            await alerts.GenerateSyncFailureAlertAsync(userId, provider, null, null, reason, SyncFailureClass.Unknown, ct);
        }
    }
}
