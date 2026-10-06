namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Recommendation-trends capture step of the nightly ingestion (feature 037, US1): tracked-set
/// filtering, key-less silence, and failure isolation (a Finnhub failure never fails the run,
/// strikes the health counter, and alerts on the second consecutive failure).
/// </summary>
public sealed class AnalystActionsIngestionJobTrendsTests
{
    [Fact]
    public async Task Capture_fetches_only_tracked_reasons_and_upserts()
    {
        var trendsService = new Mock<IRecommendationTrendsService>();
        trendsService.SetupGet(s => s.IsConfigured).Returns(true);
        trendsService
            .Setup(s => s.FetchAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RecommendationTrend { Ticker = "MU", Period = new DateOnly(2026, 8, 1) }]);
        var trendRepo = new Mock<IRecommendationTrendRepository>();
        var sut = CreateSut(trendsService.Object, trendRepo.Object, out _);

        await sut.ExecuteAsync();

        trendsService.Verify(s => s.FetchAsync(
            It.Is<IReadOnlyCollection<string>>(t =>
                t.Contains("MU") && t.Contains("NVDA") && !t.Contains("SPY")),
            It.IsAny<CancellationToken>()), Times.Once,
            "index-seed members are not swept — tracked reasons only");
        trendRepo.Verify(r => r.UpsertAsync(
            It.Is<IReadOnlyList<RecommendationTrend>>(l => l.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Capture_is_skipped_entirely_when_unconfigured()
    {
        var trendsService = new Mock<IRecommendationTrendsService>();
        trendsService.SetupGet(s => s.IsConfigured).Returns(false);
        var trendRepo = new Mock<IRecommendationTrendRepository>();
        var sut = CreateSut(trendsService.Object, trendRepo.Object, out _);

        await sut.ExecuteAsync();

        trendsService.Verify(s => s.FetchAsync(
            It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        trendRepo.Verify(r => r.UpsertAsync(
            It.IsAny<IReadOnlyList<RecommendationTrend>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Capture_failure_never_fails_the_run_and_alerts_on_second_strike()
    {
        var trendsService = new Mock<IRecommendationTrendsService>();
        trendsService.SetupGet(s => s.IsConfigured).Returns(true);
        trendsService
            .Setup(s => s.FetchAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AnalystSourceParseException("all tickers failed"));
        var trendRepo = new Mock<IRecommendationTrendRepository>();
        var sut = CreateSut(trendsService.Object, trendRepo.Object, out var alerts);

        await sut.ExecuteAsync();
        alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()), Times.Never,
            "first failure must not alert (2-strike rule)");

        await sut.ExecuteAsync();
        alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), "analyst-actions:finnhub", It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Capture_success_resets_the_failure_streak()
    {
        var configured = new Mock<IRecommendationTrendsService>();
        configured.SetupGet(s => s.IsConfigured).Returns(true);
        var calls = 0;
        configured
            .Setup(s => s.FetchAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++calls == 2
                ? Task.FromResult<IReadOnlyList<RecommendationTrend>>([])
                : Task.FromException<IReadOnlyList<RecommendationTrend>>(new AnalystSourceParseException("boom")));
        var trendRepo = new Mock<IRecommendationTrendRepository>();
        var sut = CreateSut(configured.Object, trendRepo.Object, out var alerts);

        await sut.ExecuteAsync(); // strike 1
        await sut.ExecuteAsync(); // success — resets the streak
        await sut.ExecuteAsync(); // strike 1 again (no alert — reset worked)
        await sut.ExecuteAsync(); // strike 2 — alerts exactly once

        alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            It.IsAny<Guid>(), "analyst-actions:finnhub", It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AnalystActionsIngestionJob CreateSut(
        IRecommendationTrendsService trendsService,
        IRecommendationTrendRepository trendRepository,
        out Mock<IAlertGeneratorService> alerts)
    {
        var members = new List<AnalystUniverseMember>
        {
            new() { Ticker = "MU", Reason = UniverseReason.Holding, Active = true },
            new() { Ticker = "NVDA", Reason = UniverseReason.Watchlist, Active = true },
            new() { Ticker = "SPY", Reason = UniverseReason.IndexConstituent, Active = true },
        };
        var universe = new Mock<IAnalystUniverseService>();
        universe.Setup(u => u.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(members);

        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Guid.NewGuid()]);

        alerts = new Mock<IAlertGeneratorService>();

        // No valuation coverage in these tests — the fake returns null so the step skips quietly.
        var valuation = new Mock<IValuationDataService>();
        valuation.Setup(v => v.GetCurrentMetricsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValuationCurrentMetrics?)null);

        return new AnalystActionsIngestionJob(
            universe.Object,
            [],
            new Mock<IAnalystActionRepository>().Object,
            new AnalystSourceHealth(),
            banking.Object,
            alerts.Object,
            valuation.Object,
            new Mock<IValuationSnapshotRepository>().Object,
            trendsService,
            trendRepository,
            NullLogger<AnalystActionsIngestionJob>.Instance);
    }
}
