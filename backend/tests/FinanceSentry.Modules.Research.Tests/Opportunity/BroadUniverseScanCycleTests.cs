namespace FinanceSentry.Modules.Research.Tests.Opportunity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// #558's acceptance criterion, end to end: after broad ingestion has put bars behind an index
/// constituent, a ledger-scan cycle must persist a Scan-sourced candidate for a ticker that is
/// neither held nor watchlisted, carrying both a fundamentals score and its momentum standing.
///
/// <see cref="BroadUniverseScanSeamTests"/> proves the Radar→rules half; this drives the rest of the
/// production chain — the real <see cref="OpportunityScanJob"/> over the real
/// <see cref="ScoreCandidateCommandHandler"/> and the real candidate repositories — and asserts what
/// landed in the database rather than what a stub scorer was asked to do. Only collaborators outside
/// the module under test are doubled: EDGAR (HTTP), the signal/alert/thesis writers, the brokerage
/// book, and the Risk and regime ports.
///
/// Note what "momentum standing" means in the row: the scan's rank is an ordering, not a persisted
/// column, so what proves it is the persisted excess-return RS for the ranking window together with
/// <see cref="ScanNominationRules.QualityMomentumReason"/> — a reason `RankByQualityMomentum` tags
/// only when a name carries *both* an EDGAR grade and an RS percentile.
/// </summary>
public sealed class BroadUniverseScanCycleTests
{
    private const string Constituent = BroadUniverseRadarFixture.Constituent;

    /// <summary>The scorer maps a revenue YoY of +45% to 95 — above the default top-tier bar of 80.</summary>
    private const decimal ConstituentRevenueYoy = 0.45m;
    private const int ConstituentGrade = 95;

    [Fact]
    public async Task AScanCyclePersistsAScanCandidateForANameOutsideTheBook()
    {
        var userId = Guid.NewGuid();
        var databaseName = $"scan-cycle-{Guid.NewGuid():N}";
        await using var radar = await BroadUniverseRadarFixture.CreateAsync(userId);
        await SeedCurrentIpsAsync(databaseName, userId);

        var alerts = new FakeOpportunityAlertGenerator();

        await RunScanCycleAsync(radar, databaseName, Edgar(), alerts);

        // Read back through a context sharing nothing with the cycle but the database itself, so what
        // follows is what the cycle wrote, not what its change tracker still happened to hold.
        await using var readBack = ResearchDatabase(databaseName);

        var candidate = (await readBack.OpportunityCandidates.AsNoTracking().ToListAsync())
            .Should().ContainSingle("the lagging holding and the ETF lenses are not nominatable").Subject;
        candidate.Ticker.Should().Be(Constituent);
        candidate.UserId.Should().Be(userId);
        candidate.Source.Should().Be(CandidateSource.Scan);
        candidate.Status.Should().Be(CandidateStatus.Active);
        candidate.NominationReasons.Should().Contain(
            ScanNominationRules.QualityMomentumReason, "the slot was won on quality x momentum, not momentum alone");

        var score = (await readBack.CandidateScores.AsNoTracking()
                .Where(s => s.CandidateId == candidate.Id).ToListAsync())
            .Should().ContainSingle().Subject;
        score.FundamentalsScore.Should().Be(ConstituentGrade, "the EDGAR half of the score is persisted, not just ranked on");
        score.StructureScore.Should().NotBeNull();
        score.Evidence.RsByWindow.Should().ContainKey(ScanNominationRules.RsWindowBars)
            .WhoseValue.Should().BePositive("the constituent out-ran the benchmark it is measured against");

        alerts.OpportunityAlertCalls.Should().Be(1, "a top-tier candidate outside the book is the point of the scan");
    }

    /// <summary>
    /// The scan is idempotent per candidate: a second cycle appends a score to the same candidate
    /// rather than duplicating the row, so the nightly cadence does not multiply the book.
    /// </summary>
    [Fact]
    public async Task ASecondCycleAppendsAScoreWithoutDuplicatingTheCandidate()
    {
        var userId = Guid.NewGuid();
        var databaseName = $"scan-cycle-{Guid.NewGuid():N}";
        await using var radar = await BroadUniverseRadarFixture.CreateAsync(userId);
        await SeedCurrentIpsAsync(databaseName, userId);

        await RunScanCycleAsync(radar, databaseName, Edgar(), new FakeOpportunityAlertGenerator());
        await RunScanCycleAsync(radar, databaseName, Edgar(), new FakeOpportunityAlertGenerator());

        await using var readBack = ResearchDatabase(databaseName);
        var candidate = (await readBack.OpportunityCandidates.AsNoTracking().ToListAsync())
            .Should().ContainSingle().Subject;
        candidate.NominationReasons.Should().OnlyHaveUniqueItems("re-nomination dedups reasons rather than accumulating them");
        (await readBack.CandidateScores.AsNoTracking().CountAsync()).Should().Be(2, "scores are append-only history");
    }

    /// <summary>
    /// Wires the production chain — real scan job, real scorer, real repositories — and runs one cycle
    /// against its own <see cref="ResearchDbContext"/>, the way each Hangfire run gets its own scope.
    /// A second cycle therefore re-reads its candidate from the database rather than finding the
    /// instance the first one left in a change tracker.
    /// </summary>
    private static async Task RunScanCycleAsync(
        BroadUniverseRadarFixture radar,
        string databaseName,
        ISecEdgarService edgar,
        IAlertGeneratorService alerts)
    {
        await using var research = ResearchDatabase(databaseName);
        var options = Options.Create(new OpportunityOptions());
        var ips = new IpsRepository(research);
        var scorer = new ScoreCandidateCommandHandler(
            new CandidateRepository(research),
            new CandidateScoreRepository(research),
            radar.Reader,
            edgar,
            ips,
            new FakePositionCapSource(),
            new FakeBrokerageHoldingsReader([BroadUniverseRadarFixture.HeldPosition]),
            new RecordingRadarSignalWriter(),
            new FakeMarketRegimeSource(),
            new RecordingThesisEventRecorder(),
            alerts,
            options);

        var job = new OpportunityScanJob(
            radar.Reader, edgar, ips, scorer, options, NullLogger<OpportunityScanJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);
    }

    private static RecordingSecEdgarService Edgar()
        => new(new Dictionary<string, IReadOnlyList<FundamentalFact>>(StringComparer.OrdinalIgnoreCase)
        {
            [Constituent] = RevenueGrowthFacts(Constituent, ConstituentRevenueYoy),
        });

    private static IReadOnlyList<FundamentalFact> RevenueGrowthFacts(string ticker, decimal revenueYoy)
        => [
            new(ticker, "Revenue", "Revenue", "USD", 100m * (1m + revenueYoy), new DateOnly(2026, 5, 31), "Q2", 2026, "10-Q"),
            new(ticker, "Revenue", "Revenue", "USD", 100m, new DateOnly(2025, 5, 31), "Q2", 2025, "10-Q"),
        ];

    /// <summary>The scan only scores users with a current IPS on file — this is that row.</summary>
    private static async Task SeedCurrentIpsAsync(string databaseName, Guid userId)
    {
        await using var db = ResearchDatabase(databaseName);
        db.PolicyStatements.Add(new InvestmentPolicyStatement { UserId = userId, IsCurrent = true });
        await db.SaveChangesAsync();
    }

    private static ResearchDbContext ResearchDatabase(string name)
        => new(new DbContextOptionsBuilder<ResearchDbContext>().UseInMemoryDatabase(name).Options);
}
