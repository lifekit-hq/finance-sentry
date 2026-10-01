namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// #698: a look-ahead detector that runs green but raises nothing for a full reporting cycle while
/// its inputs are non-empty is reported on the operational alert path, and the report clears once
/// the detector fires again or its inputs empty out.
/// </summary>
public sealed class LookaheadSilenceMonitorJobTests
{
    private readonly Mock<IBankingTotalsReader> _banking = new();
    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<IWatchlistReader> _watchlist = new();
    private readonly Mock<IThesisRepository> _theses = new();
    private readonly Mock<ICryptoHoldingsReader> _crypto = new();
    private readonly Mock<IAlertFireHistoryReader> _fireHistory = new();
    private readonly Mock<IRecurringJobAgeReader> _schedules = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly LookaheadSilenceMonitorJob _job;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 12, 20, 7, 0, 0, TimeSpan.Zero);

    public LookaheadSilenceMonitorJobTests()
    {
        _job = new LookaheadSilenceMonitorJob(
            _banking.Object,
            new LookaheadUniverse(_brokerage.Object, _watchlist.Object, _theses.Object, _crypto.Object),
            _fireHistory.Object,
            _schedules.Object,
            _alerts.Object,
            NullLogger<LookaheadSilenceMonitorJob>.Instance);

        _banking.Setup(b => b.GetActiveUserIdsAsync(default)).ReturnsAsync([_userId]);
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default))
            .ReturnsAsync([new BrokerageHoldingSummary("RDDT", "STK", 10m, 1500m, DateTime.UtcNow, "IBKR")]);
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync([]);
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([]);
        _crypto.Setup(c => c.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);

        // Both detectors scheduled well over a threshold ago.
        _schedules.Setup(s => s.GetCreatedAt(It.IsAny<string>())).Returns(_now.AddDays(-120));
    }

    [Fact]
    public async Task Execute_NeverFiredWithInputsForAFullCycle_ReportsBothDetectors()
    {
        _fireHistory.Setup(f => f.GetLastRaisedAtAsync(_userId, It.IsAny<string>(), default))
            .ReturnsAsync((DateTimeOffset?)null);

        await _job.ExecuteAsync(_now);

        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            _userId, EarningsAheadJob.RecurringJobId, 85, 1, default), Times.Once);
        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            _userId, FilingWatchJob.RecurringJobId, 85, 1, default), Times.Once);
    }

    [Fact]
    public async Task Execute_LastFireOlderThanTheThreshold_ReportsWithTheActualSilence()
    {
        _fireHistory.Setup(f => f.GetLastRaisedAtAsync(_userId, "FilingLanded", default))
            .ReturnsAsync(_now.AddDays(-88));
        _fireHistory.Setup(f => f.GetLastRaisedAtAsync(_userId, "EarningsAhead", default))
            .ReturnsAsync(_now.AddDays(-2));

        await _job.ExecuteAsync(_now);

        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            _userId, FilingWatchJob.RecurringJobId, 88, 1, default), Times.Once);
        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            _userId, EarningsAheadJob.RecurringJobId, It.IsAny<int>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_FiredWithinTheThreshold_ResolvesAndDoesNotReport()
    {
        _fireHistory.Setup(f => f.GetLastRaisedAtAsync(_userId, It.IsAny<string>(), default))
            .ReturnsAsync(_now.AddDays(-10));

        await _job.ExecuteAsync(_now);

        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), default), Times.Never);
        _alerts.Verify(a => a.ResolveDetectorSilentAlertAsync(_userId, EarningsAheadJob.RecurringJobId, default), Times.Once);
        _alerts.Verify(a => a.ResolveDetectorSilentAlertAsync(_userId, FilingWatchJob.RecurringJobId, default), Times.Once);
    }

    [Fact]
    public async Task Execute_EmptyInputs_ResolvesAndNeverReadsHistory()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(_userId, default)).ReturnsAsync([]);

        await _job.ExecuteAsync(_now);

        _fireHistory.Verify(f => f.GetLastRaisedAtAsync(It.IsAny<Guid>(), It.IsAny<string>(), default), Times.Never);
        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), default), Times.Never);
        _alerts.Verify(a => a.ResolveDetectorSilentAlertAsync(_userId, It.IsAny<string>(), default), Times.Exactly(2));
    }

    [Fact]
    public async Task Execute_DetectorScheduledLessThanAThresholdAgo_DoesNotReport()
    {
        // A freshly deployed detector has not had a full cycle to fire yet.
        _schedules.Setup(s => s.GetCreatedAt(It.IsAny<string>())).Returns(_now.AddDays(-30));
        _fireHistory.Setup(f => f.GetLastRaisedAtAsync(_userId, It.IsAny<string>(), default))
            .ReturnsAsync((DateTimeOffset?)null);

        await _job.ExecuteAsync(_now);

        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_DetectorNotScheduled_DoesNotReport()
    {
        _schedules.Setup(s => s.GetCreatedAt(It.IsAny<string>())).Returns((DateTimeOffset?)null);
        _fireHistory.Setup(f => f.GetLastRaisedAtAsync(_userId, It.IsAny<string>(), default))
            .ReturnsAsync((DateTimeOffset?)null);

        await _job.ExecuteAsync(_now);

        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_CountsEachDetectorsOwnInputs()
    {
        // Thesis names feed both detectors; a proxy ticker feeds only the filing detector.
        _theses.Setup(t => t.ListUnscopedAsync(_userId, default)).ReturnsAsync([
            new InvestmentThesis
            {
                UserId = _userId,
                Ticker = "ACN",
                InvalidationTriggers = [new ThesisInvalidationTrigger("revenue_growth", "lessThan", 0.02m, "IT")],
            },
        ]);
        _watchlist.Setup(w => w.ListTickersAsync(_userId, default)).ReturnsAsync(["PLTR", "TT"]);
        _fireHistory.Setup(f => f.GetLastRaisedAtAsync(_userId, It.IsAny<string>(), default))
            .ReturnsAsync((DateTimeOffset?)null);

        await _job.ExecuteAsync(_now);

        // RDDT + PLTR + TT + ACN
        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            _userId, EarningsAheadJob.RecurringJobId, 85, 4, default), Times.Once);
        // RDDT + ACN + IT
        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            _userId, FilingWatchJob.RecurringJobId, 85, 3, default), Times.Once);
    }

    [Fact]
    public async Task Execute_OneDetectorThrows_StillChecksTheOther()
    {
        _fireHistory.Setup(f => f.GetLastRaisedAtAsync(_userId, "EarningsAhead", default))
            .ThrowsAsync(new InvalidOperationException("db down"));
        _fireHistory.Setup(f => f.GetLastRaisedAtAsync(_userId, "FilingLanded", default))
            .ReturnsAsync((DateTimeOffset?)null);

        await _job.ExecuteAsync(_now);

        _alerts.Verify(a => a.GenerateDetectorSilentAlertAsync(
            _userId, FilingWatchJob.RecurringJobId, 85, 1, default), Times.Once);
    }
}
