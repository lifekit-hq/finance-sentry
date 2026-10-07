namespace FinanceSentry.Tests.Integration.Risk;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Application.Queries;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Infrastructure.Jobs;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="RiskDbContext"/>: a context acting for one person sees only that
/// person's rule sets, acknowledgements and snapshots, a context with no person in scope sees none, and the
/// daily check, the portfolio scan and the cross-module readers (which run with no person in scope) opt out
/// explicitly, so they still see the user they name. Real Postgres, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RiskOwnerQueryFilterTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly BookSnapshot Book = new(
        7000m, 0m, [new BookPosition("AAA", RiskSleeve.Brokerage, 10m, 7000m, 1m)], false, [], 7000m);

    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // Null acts as a background job: no person in scope.
    private RiskDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<RiskDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static RiskRuleSet NewRuleSet(Guid userId) =>
        new() { UserId = userId, Version = 1, IsCurrent = true, MaxPositionWeightPct = 0.25m };

    private static PolicyViolationAck NewAck(Guid userId) => new()
    {
        UserId = userId,
        RuleKey = RiskRuleKeys.MaxPositionWeight,
        Subject = "AAA",
        RemediationNote = "accepted concentration",
        ObservedAtAck = 1m,
        WorseningStepPct = 0.05m,
    };

    private static HoldingSnapshot NewSnapshot(Guid userId) => new()
    {
        UserId = userId,
        Symbol = "AAA",
        Sleeve = RiskSleeve.Brokerage,
        Quantity = 10m,
        UsdValue = 12_000m,
        CapturedAt = Now.AddDays(-30),
    };

    private async Task SeedAsync(params object[] entities)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    [DockerRequiredFact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes().Where(e => e.FindProperty("UserId") is not null).ToList();

        perUser.Should().HaveCount(3);
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_rows_and_no_person_sees_none()
    {
        await SeedAsync(
            NewRuleSet(_userA), NewRuleSet(_userB), NewAck(_userA), NewAck(_userB), NewSnapshot(_userA), NewSnapshot(_userB));

        await using (var asA = CreateContext(_userA))
        {
            (await asA.RiskRuleSets.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);
            (await asA.PolicyViolationAcks.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);
            (await asA.HoldingSnapshots.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);
        }

        await using (var asB = CreateContext(_userB))
        {
            (await asB.RiskRuleSets.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
            (await asB.PolicyViolationAcks.AnyAsync(x => x.UserId == _userA)).Should().BeFalse();
            (await asB.HoldingSnapshots.AnyAsync(x => x.UserId == _userA)).Should().BeFalse();
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.RiskRuleSets.AnyAsync()).Should().BeFalse("no person in scope matches no row");
        (await asNoOne.PolicyViolationAcks.AnyAsync()).Should().BeFalse();
        (await asNoOne.HoldingSnapshots.AnyAsync()).Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Repository_reads_follow_the_acting_person_and_the_unscoped_reads_serve_callers_without_one()
    {
        await SeedAsync(NewRuleSet(_userA), NewRuleSet(_userB), NewAck(_userA), NewSnapshot(_userA));

        await using (var asA = CreateContext(_userA))
        {
            (await new RiskRuleSetRepository(asA).GetCurrentAsync(_userA)).Should().NotBeNull();
            (await new RiskRuleSetRepository(asA).GetCurrentAsync(_userB)).Should().BeNull(
                "naming another person does not lift the owner scope");
            (await new PolicyViolationAckRepository(asA).ListActiveAsync(_userA)).Should().ContainSingle();
            (await new HoldingSnapshotRepository(asA).ListSinceAsync(_userA, Now.AddDays(-60))).Should().ContainSingle();
        }

        await using var asNoOne = CreateContext();
        var ruleSets = new RiskRuleSetRepository(asNoOne);
        (await ruleSets.GetCurrentAsync(_userA)).Should().BeNull();
        (await ruleSets.GetCurrentUnscopedAsync(_userA))!.UserId.Should().Be(_userA);
        (await ruleSets.GetUserIdsWithRuleSetsUnscopedAsync()).Should().Contain([_userA, _userB]);
        (await new PolicyViolationAckRepository(asNoOne).ListActiveUnscopedAsync(_userA)).Should().ContainSingle();
        (await new HoldingSnapshotRepository(asNoOne).ListSinceUnscopedAsync(_userA, Now.AddDays(-60)))
            .Should().ContainSingle();
    }

    [DockerRequiredFact]
    public async Task Rule_set_query_dispatched_by_a_job_with_no_person_returns_the_named_users_rules()
    {
        await SeedAsync(NewRuleSet(_userA));

        await using var asNoOne = CreateContext();
        var dto = await new GetRiskRuleSetQueryHandler(new RiskRuleSetRepository(asNoOne))
            .Handle(new GetRiskRuleSetQuery(_userA), default);

        dto!.UserId.Should().Be(_userA);
    }

    [DockerRequiredFact]
    public async Task Limits_reader_with_no_person_serves_the_portfolio_scan()
    {
        await SeedAsync(NewRuleSet(_userA));

        await using var asNoOne = CreateContext();
        var reader = new RiskLimitsReader(new RiskRuleSetRepository(asNoOne));

        (await reader.ListUserIdsWithRuleSetsAsync()).Should().Contain(_userA);
        (await reader.GetCurrentAsync(_userA))!.MaxPositionWeightPct.Should().Be(0.25m);
    }

    [DockerRequiredFact]
    public async Task Drawdown_provider_with_no_person_measures_from_the_users_snapshot_history()
    {
        await SeedAsync(NewSnapshot(_userA));
        var policy = new Mock<IDrawdownPolicySource>();
        policy.Setup(p => p.GetMaxDrawdownAsync(_userA, It.IsAny<CancellationToken>())).ReturnsAsync(0.20m);

        await using var asNoOne = CreateContext();
        var check = await new DrawdownCheckProvider(
                policy.Object, new HoldingSnapshotRepository(asNoOne), Options.Create(new RiskOptions()))
            .GetAsync(_userA, Book, Now, default);

        check.Should().NotBeNull("a filtered read would find no history and measure no decline");
        check.Value.MaxDrawdown.Should().Be(0.20m);
        check.Value.ObservedDrawdown.Should().BePositive();
    }

    [DockerRequiredFact]
    public async Task Daily_check_with_no_person_reads_the_users_rule_set_and_writes_their_snapshots()
    {
        await SeedAsync(NewRuleSet(_userA));
        var alerts = new Mock<IAlertGeneratorService>();

        await using (var asNoOne = CreateContext())
            await Job(asNoOne, alerts.Object).CheckForUserAsync(_userA);

        alerts.Verify(a => a.GeneratePolicyViolationAlertAsync(
            _userA, RiskRuleKeys.MaxPositionWeight, "AAA", It.IsAny<decimal>(), 0.25m, false, It.IsAny<CancellationToken>()),
            Times.Once, "a filtered read would find no rule set and raise nothing");

        await using var read = CreateContext(_userA);
        (await read.HoldingSnapshots.CountAsync()).Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task Daily_check_with_no_person_honours_the_users_acknowledgement()
    {
        await SeedAsync(NewRuleSet(_userA), NewAck(_userA));
        var alerts = new Mock<IAlertGeneratorService>();

        await using (var asNoOne = CreateContext())
            await Job(asNoOne, alerts.Object).CheckForUserAsync(_userA);

        alerts.Verify(a => a.GeneratePolicyViolationAlertAsync(
            _userA, RiskRuleKeys.MaxPositionWeight, It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "a filtered read would miss the acknowledgement and re-raise the alert");
    }

    private RiskCheckJob Job(RiskDbContext ctx, IAlertGeneratorService alerts)
    {
        var bookReader = new Mock<IBookSnapshotReader>();
        bookReader.Setup(r => r.ReadAsync(_userA, It.IsAny<CancellationToken>())).ReturnsAsync(Book);
        var allocation = new Mock<IAllocationPolicySource>();
        allocation.Setup(s => s.GetAllocationTargetsAsync(_userA, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var brokenTheses = new Mock<IBrokenThesisReader>();
        brokenTheses.Setup(r => r.ListBrokenAsync(_userA, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        return new RiskCheckJob(
            Mock.Of<IBankingTotalsReader>(),
            bookReader.Object,
            new RiskRuleSetRepository(ctx),
            new PolicyViolationAckRepository(ctx),
            new HoldingSnapshotRepository(ctx),
            new RiskEvaluationService(),
            allocation.Object,
            Mock.Of<IDrawdownCheckProvider>(),
            Mock.Of<ITurnoverTracker>(),
            Mock.Of<IAddToBrokenThesisDetector>(),
            brokenTheses.Object,
            alerts,
            Mock.Of<IRadarSignalWriter>(),
            Options.Create(new RiskOptions()));
    }
}
