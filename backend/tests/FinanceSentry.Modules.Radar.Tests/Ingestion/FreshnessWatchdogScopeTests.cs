namespace FinanceSentry.Modules.Radar.Tests.Ingestion;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Radar.Infrastructure.Jobs;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The freshness watchdog is an operational outage alarm. Broad-universe constituents rotate through
/// ingestion by design (#558), so their old bars must not raise it — otherwise every run alarms and
/// the book's genuinely dead data is lost in the noise.
/// </summary>
public sealed class FreshnessWatchdogScopeTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task StaleConstituentsAloneDoNotRaiseTheAlert()
    {
        var alerts = await RunAsync(Member("AAA", UniverseKind.IndexConstituent));

        alerts.Verify(
            a => a.GenerateMarketStructureFreshnessAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StaleHoldingStillRaisesTheAlert()
    {
        var alerts = await RunAsync(
            Member("AAPL", UniverseKind.Holding), Member("AAA", UniverseKind.IndexConstituent));

        alerts.Verify(
            a => a.GenerateMarketStructureFreshnessAlertAsync(
                User, It.IsAny<Guid>(), It.Is<string>(r => r.StartsWith("1 universe tickers")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NoStaleMembers_ResolvesTheAlert()
    {
        var alerts = await RunAsync(
            [Member("AAPL", UniverseKind.Holding)],
            seedFreshBarFor: "AAPL");

        alerts.Verify(
            a => a.ResolveMarketStructureFreshnessAlertAsync(User, It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
        alerts.Verify(
            a => a.GenerateMarketStructureFreshnessAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StaleHoldingStillPresent_DoesNotResolve()
    {
        var alerts = await RunAsync(Member("AAPL", UniverseKind.Holding));

        alerts.Verify(
            a => a.ResolveMarketStructureFreshnessAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NoWatchedMembers_ResolvesTheAlert()
    {
        var alerts = await RunAsync(Member("AAA", UniverseKind.IndexConstituent));

        alerts.Verify(
            a => a.ResolveMarketStructureFreshnessAlertAsync(User, It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static RadarUniverseMember Member(string ticker, UniverseKind kind) => new()
    {
        Ticker = ticker, Kind = kind, Source = UniverseSource.Auto, Active = true,
    };

    // No bars are stored for any member, so every watched ticker is stale by definition, unless
    // `seedFreshBarFor` names a ticker to backdate a fresh bar for.
    private static async Task<Mock<IAlertGeneratorService>> RunAsync(
        RadarUniverseMember[] members, string? seedFreshBarFor = null)
    {
        var universe = new Mock<IRadarUniverseRepository>();
        universe.Setup(u => u.ListActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(members);

        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([User]);

        var alerts = new Mock<IAlertGeneratorService>();

        await using var db = TestSupport.NewContext();
        if (seedFreshBarFor is not null)
        {
            db.DailyBars.Add(new DailyBar
            {
                Id = Guid.NewGuid(),
                Ticker = seedFreshBarFor,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
            });
            await db.SaveChangesAsync();
        }

        var job = new RadarFreshnessWatchdogJob(
            universe.Object,
            new DailyBarRepository(db),
            banking.Object,
            alerts.Object,
            TestSupport.Options(),
            NullLogger<RadarFreshnessWatchdogJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);
        return alerts;
    }

    private static Task<Mock<IAlertGeneratorService>> RunAsync(params RadarUniverseMember[] members)
        => RunAsync(members, seedFreshBarFor: null);
}
