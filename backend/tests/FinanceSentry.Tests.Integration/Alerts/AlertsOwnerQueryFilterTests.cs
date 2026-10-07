namespace FinanceSentry.Tests.Integration.Alerts;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Infrastructure.Jobs;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="AlertsDbContext"/>: a context acting for one person sees only that
/// person's alerts, a context with no person in scope sees none, and every background path that legitimately
/// reads across users (the expiry and purge sweeps, the generator's dedup/resolve/delete path, the companion
/// capture reader, the fire-history reader) opts out explicitly, so none of them silently sees nothing.
/// Real Postgres is required: the purge and occurrence bump run as <c>ExecuteDelete</c>/<c>ExecuteUpdate</c>,
/// which the in-memory provider does not support.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AlertsOwnerQueryFilterTests : IAsyncLifetime
{
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
    private AlertsDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private async Task SeedAsync(params Alert[] alerts)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.Alerts.AddRange(alerts);
        await seed.SaveChangesAsync();
    }

    private static Alert NewAlert(
        Guid userId, string type = AlertType.LowBalance, Guid? referenceId = null, DateTimeOffset? createdAt = null) => new()
    {
        UserId = userId,
        Type = type,
        Severity = AlertSeverity.Warning,
        Title = type,
        Message = "m",
        ReferenceId = referenceId ?? Guid.NewGuid(),
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
    };

    private async Task<List<Alert>> AllRowsAsync()
    {
        await using var read = CreateContext();
        return await read.Alerts.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(a => a.UserId == _userA || a.UserId == _userB)
            .ToListAsync();
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_alerts_and_no_person_sees_none()
    {
        var a = NewAlert(_userA);
        var b = NewAlert(_userB);
        await SeedAsync(a, b);

        await using (var asA = CreateContext(_userA))
            (await asA.Alerts.Select(x => x.Id).ToListAsync()).Should().Equal(a.Id);

        await using (var asB = CreateContext(_userB))
        {
            (await asB.Alerts.Select(x => x.Id).ToListAsync()).Should().Equal(b.Id);
            (await asB.Alerts.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.Alerts.AnyAsync()).Should().BeFalse("no person in scope matches no row");
    }

    [DockerRequiredFact]
    public async Task Request_path_writes_cannot_reach_another_persons_alerts()
    {
        var b = NewAlert(_userB);
        await SeedAsync(b);

        await using (var asA = CreateContext(_userA))
        {
            var repository = new AlertRepository(asA);
            await repository.MarkAllReadAsync(_userB);
            (await repository.DismissAsync(_userB, b.Id)).Should().BeFalse();
        }

        var stored = (await AllRowsAsync()).Should().ContainSingle().Subject;
        stored.IsRead.Should().BeFalse();
        stored.IsDismissed.Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Expiry_job_with_no_person_resolves_expired_alerts_of_every_user()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-10);
        await SeedAsync(NewAlert(_userA, AlertType.NewsCluster, createdAt: old), NewAlert(_userB, AlertType.NewsCluster, createdAt: old));

        await using (var ctx = CreateContext())
        {
            var job = new AlertExpiryJob(new AlertRepository(ctx), TimeProvider.System, NullLogger<AlertExpiryJob>.Instance);
            await job.ExecuteAsync();
        }

        var rows = await AllRowsAsync();
        rows.Should().HaveCount(2).And.OnlyContain(r => r.IsResolved);
    }

    [DockerRequiredFact]
    public async Task Purge_job_with_no_person_purges_old_closed_alerts_of_every_user()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-100);
        var a = NewAlert(_userA, createdAt: old);
        var b = NewAlert(_userB, createdAt: old);
        a.IsResolved = true;
        b.IsDismissed = true;
        await SeedAsync(a, b);

        await using (var ctx = CreateContext())
        {
            var job = new AlertPurgeJob(new AlertRepository(ctx), NullLogger<AlertPurgeJob>.Instance);
            await job.ExecuteAsync();
        }

        (await AllRowsAsync()).Should().BeEmpty();
    }

    [DockerRequiredFact]
    public async Task Generator_with_no_person_dedups_resolves_and_deletes_the_named_users_alerts()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        await SeedAsync(NewAlert(_userA, referenceId: accountA), NewAlert(_userB, referenceId: accountB));

        await using (var ctx = CreateContext())
        {
            var generator = new AlertGeneratorService(new AlertRepository(ctx), new Mock<IPolicyAckReader>().Object);

            // An open alert on the same reference is found, so the repeat bumps it instead of inserting a duplicate.
            await generator.GenerateLowBalanceAlertAsync(_userA, accountA, "Checking", 10m, 100m);
            await generator.ResolveLowBalanceAlertAsync(_userB, accountB);
        }

        var rows = await AllRowsAsync();
        rows.Should().HaveCount(2);
        rows.Single(r => r.UserId == _userA).OccurrenceCount.Should().Be(2);
        rows.Single(r => r.UserId == _userB).IsResolved.Should().BeTrue();

        await using (var ctx = CreateContext())
        {
            var generator = new AlertGeneratorService(new AlertRepository(ctx), new Mock<IPolicyAckReader>().Object);
            await generator.DeleteAlertsForAccountAsync(accountA);
        }

        (await AllRowsAsync()).Should().ContainSingle(r => r.UserId == _userB);
    }

    [DockerRequiredFact]
    public async Task Companion_capture_reader_with_no_person_reads_every_users_new_alerts()
    {
        var watermark = DateTimeOffset.UtcNow.AddMinutes(-1);
        var a = NewAlert(_userA);
        var b = NewAlert(_userB);
        await SeedAsync(a, b);

        await using var ctx = CreateContext();
        var records = await new MaterialAlertReader(ctx).GetNewSinceAsync(watermark, 100);

        records.Select(r => r.AlertId).Should().BeEquivalentTo([a.Id, b.Id]);
    }

    [DockerRequiredFact]
    public async Task Fire_history_reader_with_no_person_reads_the_named_users_history()
    {
        var raisedAt = DateTimeOffset.UtcNow.AddHours(-2);
        await SeedAsync(NewAlert(_userA, AlertType.EarningsAhead, createdAt: raisedAt), NewAlert(_userB, AlertType.EarningsAhead));

        await using var ctx = CreateContext();
        var last = await new AlertFireHistoryReader(ctx).GetLastRaisedAtAsync(_userA, AlertType.EarningsAhead);

        last.Should().BeCloseTo(raisedAt, TimeSpan.FromMilliseconds(1));
    }
}
