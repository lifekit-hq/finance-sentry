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
/// #558's acceptance criterion, end to end: a ledger-scan cycle must persist a Scan-sourced candidate
/// for a ticker that is neither held nor watchlisted, that entered through the stage-1 shortlist, and
/// that carries both a fundamentals score and its momentum standing.
///
/// <see cref="BroadUniverseScanSeamTests"/> proves the Radar→rules half; this drives the rest of the
/// production chain — the real <see cref="OpportunityScanJob"/> over the real
/// <see cref="ScoreCandidateCommandHandler"/> and the real candidate repositories — and asserts what
/// landed in the database rather than what a stub scorer was asked to do. Only collaborators outside
/// the module under test are doubled: EDGAR (HTTP), the signal/alert/thesis writers, the brokerage
/// book, and the Risk and regime ports.
///
/// The cycle runs *through* the funnel, not past it: <see cref="BroadUniverseRadarFixture"/> composes
/// its universe out of a real stage-1 shortlist run, and both stages grade against the one EDGAR
/// double, the way production shares one cached service between them.
///
/// Note what "momentum standing" means in the row: the scan's rank is an ordering, not a persisted
/// column, so what proves it is the persisted excess-return RS for the ranking window together with
/// <see cref="ScanNominationRules.QualityMomentumReason"/> — a reason `RankByQualityMomentum` tags
/// only when a name carries *both* an EDGAR grade and an RS percentile.
/// </summary>
public sealed class BroadUniverseScanCycleTests
{
    private const int ConstituentGrade = BroadUniverseRadarFixture.ConstituentGrade;

    [Fact]
    public async Task AScanCyclePersistsAScanCandidateForANameOutsideTheBook()
    {
        var userId = Guid.NewGuid();
        var databaseName = $"scan-cycle-{Guid.NewGuid():N}";
        await using var radar = await BroadUniverseRadarFixture.CreateAsync(userId);
        await SeedCurrentIpsAsync(databaseName, userId);

        var alerts = new FakeOpportunityAlertGenerator();
        await RunScanCycleAsync(radar, databaseName, alerts);

        // Read back through a context sharing nothing with the cycle but the database itself, so what
        // follows is what the cycle wrote, not what its change tracker still happened to hold.
        await using var readBack = ResearchDatabase(databaseName);

        var candidate = (await readBack.OpportunityCandidates.AsNoTracking().ToListAsync())
            .Should().ContainSingle("the lagging holding and the ETF lenses are not nominatable").Subject;
        candidate.UserId.Should().Be(userId);
        candidate.Source.Should().Be(CandidateSource.Scan);
        candidate.Status.Should().Be(CandidateStatus.Active);
        candidate.NominationReasons.Should().Contain(
            ScanNominationRules.QualityMomentumReason, "the slot was won on quality x momentum, not momentum alone");

        // Clause 5 is a claim about *how* the surviving name got here, so it is asserted over the
        // candidate's provenance rather than over its identity: outside the book, and on the list
        // stage 1 actually returned. Naming the expected ticker first would make both checks follow
        // from the fixture instead of from the cycle.
        candidate.Ticker.Should().NotBe(BroadUniverseRadarFixture.Holding,
            "re-nominating the book is the behaviour #558 was filed against");
        radar.Shortlist.Should().Contain(candidate.Ticker,
            "the only non-book route into the universe is the stage-1 shortlist");

        var score = (await readBack.CandidateScores.AsNoTracking()
                .Where(s => s.CandidateId == candidate.Id).ToListAsync())
            .Should().ContainSingle().Subject;
        score.FundamentalsScore.Should().Be(ConstituentGrade, "the EDGAR half of the score is persisted, not just ranked on");
        score.StructureScore.Should().NotBeNull();
        score.Evidence.RsByWindow.Should().ContainKey(ScanNominationRules.RsWindowBars)
            .WhoseValue.Should().BePositive("the constituent out-ran the benchmark it is measured against");

        alerts.OpportunityAlertCalls.Should().Be(0,
            "this candidate clears the top-tier bar, and the scan still ships in the log-only posture "
            + "clause 4 asks for — the finding lands as a signal, not as an interruption");
    }

    /// <summary>
    /// Clause 4's flood guard, seen from the cycle rather than the handler: the same top-tier
    /// nomination the launch posture only records does interrupt once the mode is opened, so the guard
    /// is a switch and not a dead lane. <see cref="ScanAlertModeTests"/> pins the gate's own rules.
    /// </summary>
    [Fact]
    public async Task ATopTierScanCandidateAlertsOnceTheModeIsOpened()
    {
        var userId = Guid.NewGuid();
        var databaseName = $"scan-cycle-{Guid.NewGuid():N}";
        await using var radar = await BroadUniverseRadarFixture.CreateAsync(userId);
        await SeedCurrentIpsAsync(databaseName, userId);

        var alerts = new FakeOpportunityAlertGenerator();
        await RunScanCycleAsync(radar, databaseName, alerts, ScanAlertMode.Alerting);

        alerts.OpportunityAlertCalls.Should().Be(1);
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

        await RunScanCycleAsync(radar, databaseName, new FakeOpportunityAlertGenerator());
        await RunScanCycleAsync(radar, databaseName, new FakeOpportunityAlertGenerator());

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
    ///
    /// Stage 2 grades through the fixture's EDGAR double — the same instance stage 1 screened on, as
    /// production shares one cached <see cref="ISecEdgarService"/> across the funnel. A null
    /// <paramref name="alertMode"/> leaves the production default in place, so the acceptance test's
    /// posture is the shipped one rather than the test's own.
    /// </summary>
    private static async Task RunScanCycleAsync(
        BroadUniverseRadarFixture radar,
        string databaseName,
        IAlertGeneratorService alerts,
        ScanAlertMode? alertMode = null)
    {
        await using var research = ResearchDatabase(databaseName);
        var settings = new OpportunityOptions();
        if (alertMode is { } mode)
        {
            settings.ScanAlertMode = mode;
        }

        var options = Options.Create(settings);
        var edgar = radar.Edgar;
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
