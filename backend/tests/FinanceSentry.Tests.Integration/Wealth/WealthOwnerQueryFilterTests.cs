namespace FinanceSentry.Tests.Integration.Wealth;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="WealthDbContext"/>: a context acting for one person sees only that
/// person's net-worth snapshots, a context with no person in scope sees none, and the snapshot job, the startup
/// catch-up, the cross-module brokerage history reader and the manual wealth source (which run with no person in
/// scope) opt out explicitly, so they still see the user they name. Real Postgres, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WealthOwnerQueryFilterTests : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

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
    private WealthDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<WealthDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static NetWorthSnapshot NewSnapshot(Guid userId, DateOnly date, decimal crypto = 0m) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        SnapshotDate = date,
        BankingTotal = 1_000m,
        BrokerageTotal = 300m,
        CryptoTotal = crypto,
        TotalNetWorth = 1_300m + crypto,
        Currency = "USD",
        TakenAt = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
    };

    private async Task SeedAsync(params NetWorthSnapshot[] snapshots)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.NetWorthSnapshots.AddRange(snapshots);
        await seed.SaveChangesAsync();
    }

    private async Task<List<NetWorthSnapshot>> RowsOfAsync(Guid userId)
    {
        await using var read = CreateContext(userId);
        return await read.NetWorthSnapshots.OrderBy(s => s.SnapshotDate).ToListAsync();
    }

    [DockerRequiredFact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        // Every entity type, not only those with a UserId: a source row has none and is filtered through its snapshot.
        var perUser = ctx.Model.GetEntityTypes().ToList();

        perUser.Should().HaveCount(3);
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_rows_and_no_person_sees_none()
    {
        await SeedAsync(NewSnapshot(_userA, Today), NewSnapshot(_userB, Today));

        await using (var asA = CreateContext(_userA))
            (await asA.NetWorthSnapshots.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);

        await using (var asB = CreateContext(_userB))
        {
            (await asB.NetWorthSnapshots.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.NetWorthSnapshots.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task Source_rows_follow_their_snapshots_owner_and_no_person_sees_none()
    {
        var snapshotA = NewSnapshot(_userA, Today);
        var snapshotB = NewSnapshot(_userB, Today);
        await SeedAsync(snapshotA, snapshotB);
        await using (var seed = CreateContext())
        {
            seed.NetWorthSnapshotSources.AddRange(
                new NetWorthSnapshotSource { Id = Guid.NewGuid(), SnapshotId = snapshotA.Id, SourceId = "banking", Kind = EconomyKind.Bank, Total = 1_000m, Health = SourceHealth.Complete },
                new NetWorthSnapshotSource { Id = Guid.NewGuid(), SnapshotId = snapshotB.Id, SourceId = "banking", Kind = EconomyKind.Bank, Total = 2_000m, Health = SourceHealth.Complete });
            await seed.SaveChangesAsync();
        }

        await using (var asA = CreateContext(_userA))
            (await asA.NetWorthSnapshotSources.Select(x => x.Total).ToListAsync()).Should().Equal(1_000m);

        await using (var asB = CreateContext(_userB))
            (await asB.NetWorthSnapshotSources.AnyAsync(x => x.SnapshotId == snapshotA.Id)).Should().BeFalse(
                "the filter holds even when a query names another person's snapshot");

        await using var asNoOne = CreateContext();
        (await asNoOne.NetWorthSnapshotSources.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task Source_row_reads_follow_the_acting_person_and_the_unscoped_read_names_its_user()
    {
        var snapshotA = NewSnapshot(_userA, Today);
        var snapshotB = NewSnapshot(_userB, Today);
        await SeedAsync(snapshotA, snapshotB);
        await using (var seed = CreateContext())
        {
            seed.NetWorthSnapshotSources.AddRange(
                new NetWorthSnapshotSource { Id = Guid.NewGuid(), SnapshotId = snapshotA.Id, SourceId = "banking", Kind = EconomyKind.Bank, Total = 1_000m, Health = SourceHealth.Complete },
                new NetWorthSnapshotSource { Id = Guid.NewGuid(), SnapshotId = snapshotB.Id, SourceId = "banking", Kind = EconomyKind.Bank, Total = 2_000m, Health = SourceHealth.Complete });
            await seed.SaveChangesAsync();
        }

        Guid[] both = [snapshotA.Id, snapshotB.Id];
        await using (var asA = CreateContext(_userA))
        {
            var repo = new NetWorthSnapshotRepository(asA);
            (await repo.GetSourcesAsync(_userA, both)).Select(r => r.Total).Should().Equal(1_000m);
            (await repo.GetSourcesAsync(_userB, both)).Should().BeEmpty("naming another person does not lift the owner scope");
        }

        await using var asNoOne = CreateContext();
        var unscoped = new NetWorthSnapshotRepository(asNoOne);
        (await unscoped.GetSourcesAsync(_userA, both)).Should().BeEmpty();
        (await unscoped.GetSourcesUnscopedAsync(_userA, both)).Select(r => r.Total).Should().Equal(
            [1_000m], "the unscoped read keeps its own user predicate");
        (await unscoped.GetSourcesUnscopedAsync(_userA, [])).Should().BeEmpty();
    }

    [DockerRequiredFact]
    public async Task Manual_holdings_follow_the_acting_person_and_the_wealth_source_reads_the_named_user_with_no_person()
    {
        await using (var seed = CreateContext())
        {
            seed.ManualHoldings.AddRange(
                ManualHolding.Create(_userA, "Steam wallet", 40m, "USD", Today, Convertibility.InEconomy),
                ManualHolding.Create(_userB, "Steam wallet", 90m, "USD", Today, Convertibility.InEconomy));
            await seed.SaveChangesAsync();
        }

        await using (var asA = CreateContext(_userA))
        {
            var repo = new ManualHoldingRepository(asA);
            (await repo.ListAsync(_userA)).Select(h => h.Amount).Should().Equal(40m);
            (await repo.ListAsync(_userB)).Should().BeEmpty("naming another person does not lift the owner scope");
            var other = (await CreateContext().ManualHoldings.IgnoreQueryFilters().FirstAsync(h => h.UserId == _userB)).Id;
            (await repo.GetAsync(_userB, other)).Should().BeNull();
        }

        await using var asNoOne = CreateContext();
        var unscoped = new ManualHoldingRepository(asNoOne);
        (await unscoped.ListAsync(_userA)).Should().BeEmpty("no person in scope matches no row");
        var reading = await new ManualWealthSource(unscoped).ReadAsync(_userA);
        reading.TotalValueUsd.Should().Be(40m, "the snapshot job reads the named user's entries with no person in scope");
        reading.Reliability.Should().Be(Reliability.L3Estimated);
    }

    [DockerRequiredFact]
    public async Task Snapshot_job_with_no_person_writes_one_row_per_registered_source()
    {
        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetTotalUsdAsync(_userA, It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        var crypto = new Mock<ICryptoHoldingsReader>();
        crypto.Setup(c => c.GetHoldingsAsync(_userA, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(_userA, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var book = new BookFigures(0m, 0m, 0m, 0m, 0m, [], false, [])
        {
            Sources = [new BookSourceReading("banking", EconomyKind.Bank, new WealthSourceReading(1_250m, []))],
        };

        await using (var asNoOne = CreateContext())
        {
            var repo = new NetWorthSnapshotRepository(asNoOne);
            var job = new NetWorthSnapshotJob(banking.Object, crypto.Object, brokerage.Object, new NetWorthSnapshotService(repo),
                Mock.Of<IBookFiguresService>(b => b.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(book)));
            await job.CaptureForUserAsync(_userA, Today);
        }

        await using var asA = CreateContext(_userA);
        var rows = await asA.NetWorthSnapshotSources.ToListAsync();
        rows.Should().ContainSingle().Which.Total.Should().Be(1_250m);
    }

    [DockerRequiredFact]
    public async Task Repository_reads_follow_the_acting_person_and_the_unscoped_reads_serve_callers_without_one()
    {
        await SeedAsync(NewSnapshot(_userA, Today.AddDays(-1)), NewSnapshot(_userA, Today));

        await using (var asA = CreateContext(_userA))
        {
            var repo = new NetWorthSnapshotRepository(asA);
            (await repo.GetByUserIdAsync(_userA, null, null)).Should().HaveCount(2);
            (await repo.GetLatestByUserIdAsync(_userA))!.SnapshotDate.Should().Be(Today);
            (await repo.GetEarliestByUserIdAsync(_userA))!.SnapshotDate.Should().Be(Today.AddDays(-1));
        }

        await using (var asB = CreateContext(_userB))
        {
            var repo = new NetWorthSnapshotRepository(asB);
            (await repo.GetByUserIdAsync(_userA, null, null)).Should().BeEmpty(
                "naming another person does not lift the owner scope");
            (await repo.GetLatestByUserIdAsync(_userA)).Should().BeNull();
        }

        await using var asNoOne = CreateContext();
        var unscoped = new NetWorthSnapshotRepository(asNoOne);
        (await unscoped.GetByUserIdAsync(_userA, null, null)).Should().BeEmpty();
        (await unscoped.GetByUserIdUnscopedAsync(_userA, null, null)).Should().HaveCount(2);
        (await unscoped.GetLatestByUserIdUnscopedAsync(_userA))!.SnapshotDate.Should().Be(Today);
        (await unscoped.GetLatestBeforeUnscopedAsync(_userA, Today))!.SnapshotDate.Should().Be(Today.AddDays(-1));
    }

    [DockerRequiredFact]
    public async Task Snapshot_job_with_no_person_carries_forward_and_refreshes_the_days_row()
    {
        await SeedAsync(NewSnapshot(_userA, Today.AddDays(-1), crypto: 500m));

        // Two runs on the same day with a stale crypto feed: the first must carry yesterday's crypto forward,
        // the second must replace today's row rather than collide with it on the unique (user, date) index.
        foreach (var banking in new[] { 1_100m, 1_200m })
        {
            await using var asNoOne = CreateContext();
            await new NetWorthSnapshotService(new NetWorthSnapshotRepository(asNoOne)).PersistSnapshotAsync(
                _userA, new NetWorthSnapshotData(Today, banking, 300m, 0m, CryptoFresh: false));
        }

        var today = (await RowsOfAsync(_userA)).Should().HaveCount(2).And.Subject.Last();
        today.BankingTotal.Should().Be(1_200m);
        today.CryptoTotal.Should().Be(500m, "a filtered read would find no baseline and record crypto as $0");
        today.StaleSleeves.Should().Be("crypto");
    }

    [DockerRequiredFact]
    public async Task Startup_catch_up_with_no_person_fills_the_gap_since_the_users_latest_snapshot()
    {
        await SeedAsync(NewSnapshot(_userA, Today.AddDays(-3)));

        var banking = new Mock<IBankingTotalsReader>();
        banking.Setup(b => b.GetActiveUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_userA]);
        var crypto = new Mock<ICryptoHoldingsReader>();
        crypto.Setup(c => c.GetHoldingsAsync(_userA, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(b => b.GetHoldingsAsync(_userA, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await using (var asNoOne = CreateContext())
        {
            var repo = new NetWorthSnapshotRepository(asNoOne);
            var job = new NetWorthSnapshotJob(banking.Object, crypto.Object, brokerage.Object, new NetWorthSnapshotService(repo),
                Mock.Of<IBookFiguresService>(b => b.ReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(BookFigures.Empty)));
            await new NetWorthSnapshotBackfillService(banking.Object, repo, job).BackfillAsync();
        }

        (await RowsOfAsync(_userA)).Select(s => s.SnapshotDate).Should().Equal(
            [Today.AddDays(-3), Today.AddDays(-2), Today.AddDays(-1), Today],
            "a filtered read would find no latest snapshot and capture today only");
    }

    [DockerRequiredFact]
    public async Task Brokerage_history_reader_with_no_person_serves_the_named_users_days()
    {
        await SeedAsync(NewSnapshot(_userA, Today.AddDays(-1)), NewSnapshot(_userA, Today), NewSnapshot(_userB, Today));

        await using var asNoOne = CreateContext();
        var days = await new BrokerageValueHistoryReader(new NetWorthSnapshotRepository(asNoOne))
            .GetDailyAsync(_userA, Today.AddDays(-7), Today);

        days.Select(d => d.Date).Should().Equal(Today.AddDays(-1), Today);
    }
}
