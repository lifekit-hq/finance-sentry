namespace FinanceSentry.Tests.Unit.Radar;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FinanceSentry.Modules.Radar.Application.Commands;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.MarketStructure;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// The nightly Radar unusual-move alert keeps its dedup: an unread MarketStructure alert already open
/// for a held ticker suppresses the next night's repeat, even once the 24h silence window has passed.
/// Runs the real handler through the real <see cref="AlertGeneratorService"/>, so a change of the
/// Radar call site's dedup mode (e.g. to <see cref="AlertDedup.SilenceOnly"/>) fails here. The
/// intraday-move sentinel is the counterpart that deliberately lets such a repeat through.
/// </summary>
public sealed class ComputeMarketStructureAlertDedupTests
{
    private const string Ticker = "AAPL";

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IAlertRepository> _alertRepo = new();
    private readonly ComputeMarketStructureCommandHandler _handler;

    public ComputeMarketStructureAlertDedupTests()
    {
        var structure = new Mock<IStructureQueryService>();
        structure.Setup(s => s.GetStructuresAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([UnusualMove(Ticker, zScore: 5m)]);
        structure.Setup(s => s.GetBreadthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BreadthResult(null, null, null, 0));
        structure.Setup(s => s.GetSectorRotationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var signals = new Mock<IRadarSignalWriter>();
        signals.Setup(s => s.AppendSignalAsync(It.IsAny<RadarSignalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_userId]);

        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BrokerageHoldingSummary(Ticker, "STK", 10m, 2000m, DateTime.UtcNow, "IBKR")]);

        // The prior alert is outside the 24h window, so only an open-alert check can suppress.
        _alertRepo.Setup(r => r.HasRecentAsync(
                _userId, AlertType.MarketStructure, It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _handler = new ComputeMarketStructureCommandHandler(
            structure.Object,
            signals.Object,
            banking.Object,
            brokerage.Object,
            new AlertGeneratorService(_alertRepo.Object),
            Mock.Of<IDailyBarRepository>(),
            Mock.Of<IRadarUniverseRepository>(),
            Options.Create(new RadarOptions { ScannerMode = ScannerMode.Alerting }));
    }

    [Fact]
    public async Task HeldUnusualMove_WithAnUnreadAlertAlreadyOpen_IsSuppressed()
    {
        _alertRepo.Setup(r => r.FindActiveAsync(
                _userId, AlertType.MarketStructure, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Alert { Id = Guid.NewGuid(), UserId = _userId, Type = AlertType.MarketStructure });

        await _handler.Handle(new ComputeMarketStructureCommand(), CancellationToken.None);

        _alertRepo.Verify(r => r.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Control: without an open alert the same run does raise one, so the suppression above is real.</summary>
    [Fact]
    public async Task HeldUnusualMove_WithNoOpenAlert_RaisesOne()
    {
        _alertRepo.Setup(r => r.FindActiveAsync(
                _userId, AlertType.MarketStructure, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Alert?)null);

        await _handler.Handle(new ComputeMarketStructureCommand(), CancellationToken.None);

        _alertRepo.Verify(r => r.AddAsync(
            It.Is<Alert>(a => a.Type == AlertType.MarketStructure && a.ReferenceLabel == Ticker),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TickerStructure UnusualMove(string ticker, decimal zScore)
        => new(
            ticker,
            new Dictionary<int, decimal?>(),
            new Dictionary<int, decimal?>(),
            Ma20: null,
            Ma50: null,
            Ma200: null,
            ExtensionFromMa50: null,
            Vol63: 0.02m,
            TodayZScore: zScore,
            VolumeRatio: null,
            Stale: false);
}
