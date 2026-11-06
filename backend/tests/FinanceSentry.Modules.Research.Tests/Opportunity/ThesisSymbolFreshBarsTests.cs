namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Application.Commands;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Infrastructure.Jobs;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar.Tests;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// #1322 end to end with production parts: a thesis stored in the Research database is read back
/// through the real <see cref="ThesisTickerReader"/> (no person in scope, as in the ingestion job),
/// composed into the real <see cref="RadarUniverseService"/>, fetched by the real ingestion handler and
/// judged by the real freshness watchdog. Only the upstream price feed and the alert sink are doubles.
/// </summary>
public sealed class ThesisSymbolFreshBarsTests
{
    private const string ThesisOnly = "TSM";
    private const string Held = "HELD";
    private const string Benchmark = "SPY";

    [Fact]
    public async Task ASymbolWithAThesisOutsideEveryOtherSourceGetsFreshBarsAndNoStaleVerdict()
    {
        var owner = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        await using var research = NewResearchDb();
        research.Theses.AddRange(
            new InvestmentThesis { UserId = owner, Ticker = " tsm ", ThesisText = "Foundry moat." },
            new InvestmentThesis { UserId = otherUser, Ticker = "OTHR", ThesisText = "Someone else's." });
        await research.SaveChangesAsync();

        await using var radar = NewRadarDb();
        var (ingest, watchdog, alerts, history) = Compose(research, radar, owner);

        var summary = await ingest.Handle(new IngestDailyBarsCommand(), CancellationToken.None);

        summary.Errors.Should().Be(0);
        history.Requested.Should().Contain(ThesisOnly, "a thesis symbol must get price bars fetched");
        history.Requested.Should().NotContain("OTHR", "another person's thesis never widens this book's universe");
        var members = await new InMemoryRadarUniverseRepository(radar).ListActiveAsync();
        members.Single(m => m.Ticker == ThesisOnly).Kind.Should().Be(UniverseKind.Thesis);

        await watchdog.ExecuteAsync();

        alerts.Verify(
            a => a.GenerateMarketStructureFreshnessAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a thesis symbol with fresh bars must not read as stale");
        alerts.Verify(
            a => a.ResolveMarketStructureFreshnessAlertAsync(owner, It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WithoutAThesisTheSameSymbolIsNeverFetched()
    {
        var owner = Guid.NewGuid();
        await using var research = NewResearchDb();
        await using var radar = NewRadarDb();
        var (ingest, _, _, history) = Compose(research, radar, owner);

        await ingest.Handle(new IngestDailyBarsCommand(), CancellationToken.None);

        history.Requested.Should().NotContain(ThesisOnly, "only the thesis put it into the universe");
        history.Requested.Should().Contain(Held);
    }

    private static RadarDbContext NewRadarDb() => new(
        new DbContextOptionsBuilder<RadarDbContext>().UseInMemoryDatabase($"thesis-radar-{Guid.NewGuid():N}").Options,
        NoCurrentUser.Instance);

    private sealed class FakeHistorySource(IReadOnlyDictionary<string, IReadOnlyList<DailyBarData>> barsByTicker)
        : IMarketHistorySource
    {
        public List<string> Requested { get; } = [];

        public Task<IReadOnlyList<DailyBarData>> GetDailyBarsAsync(
            string ticker, DateOnly since, CancellationToken ct = default)
        {
            Requested.Add(ticker);
            return Task.FromResult(barsByTicker.TryGetValue(ticker, out var b) ? b : []);
        }
    }

    private static ResearchDbContext NewResearchDb()
    {
        var options = new DbContextOptionsBuilder<ResearchDbContext>()
            .UseInMemoryDatabase($"thesis-universe-{Guid.NewGuid():N}")
            .Options;
        return new ResearchDbContext(options, NoCurrentUser.Instance);
    }

    private static (IngestDailyBarsCommandHandler Ingest, RadarFreshnessWatchdogJob Watchdog,
        Mock<IAlertGeneratorService> Alerts, FakeHistorySource History) Compose(
            ResearchDbContext research, RadarDbContext radar, Guid owner)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RadarOptions
        {
            Benchmark = Benchmark,
            BenchmarkTickers = [Benchmark],
            SectorTickers = [],
            IndustryTickers = [],
            BroadUniverseEnabled = false,
        });

        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([owner]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BrokerageHoldingSummary(Held, "STK", 1m, 100m, DateTime.UtcNow, "IBKR")]);
        var watchlist = new Mock<IWatchlistReader>();
        watchlist.Setup(w => w.ListTickersAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var universeRepo = new InMemoryRadarUniverseRepository(radar);
        var barRepo = new InMemoryDailyBarRepository(radar);
        var universe = new RadarUniverseService(
            universeRepo, brokerage.Object, watchlist.Object,
            new ThesisTickerReader(new ThesisRepository(research)), banking.Object, options);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bars = new[] { Benchmark, Held, ThesisOnly, "OTHR" }.ToDictionary(
            t => t,
            _ => (IReadOnlyList<DailyBarData>)[new DailyBarData(today, 10m, 11m, 9m, 10m, 10m, 1_000)]);
        var history = new FakeHistorySource(bars);

        var ingest = new IngestDailyBarsCommandHandler(
            universe, history, barRepo, options, NullLogger<IngestDailyBarsCommandHandler>.Instance);

        var alerts = new Mock<IAlertGeneratorService>();
        var watchdog = new RadarFreshnessWatchdogJob(
            universeRepo, barRepo, banking.Object, alerts.Object, options,
            NullLogger<RadarFreshnessWatchdogJob>.Instance);

        return (ingest, watchdog, alerts, history);
    }
}
