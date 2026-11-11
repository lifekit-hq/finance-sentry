namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// #1375: the monitor's last-run coverage snapshot is written with a targeted <c>ExecuteUpdate</c> through the jsonb
/// conversion, past the Owner filter (the job has no principal), for the named user only, and without touching
/// <c>UpdatedAt</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ThesisMonitorSnapshotPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset RunAt = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

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

    private ResearchDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private async Task<InvestmentThesis> SeedAsync(Guid userId)
    {
        await using var db = CreateContext();
        var thesis = new InvestmentThesis { UserId = userId, Ticker = "MU", ThesisText = "t" };
        db.Theses.Add(thesis);
        await db.SaveChangesAsync();
        return thesis;
    }

    private async Task<InvestmentThesis> ReadAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.Theses.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync(t => t.Id == id);
    }

    [DockerRequiredFact]
    public async Task A_snapshot_written_with_no_principal_reads_back_and_leaves_UpdatedAt_alone()
    {
        var thesis = await SeedAsync(Guid.NewGuid());
        var before = (await ReadAsync(thesis.Id)).UpdatedAt;
        var snapshot = new ThesisMonitorSnapshot(RunAt, 3, ["gross_margin|MU|Quarter|lessThan|1||", "revenue_yoy|MU|Quarter|lessThan|1||"]);

        await using (var db = CreateContext())
        {
            await new ThesisRepository(db).SaveMonitorSnapshotAsync(thesis.UserId, thesis.Id, snapshot);
        }

        var stored = await ReadAsync(thesis.Id);
        stored.MonitorSnapshot.Should().NotBeNull();
        stored.MonitorSnapshot!.At.Should().Be(RunAt);
        stored.MonitorSnapshot.TotalTriggers.Should().Be(3);
        stored.MonitorSnapshot.EvaluableKeys.Should().Equal(snapshot.EvaluableKeys);
        stored.UpdatedAt.Should().Be(before);
    }

    [DockerRequiredFact]
    public async Task A_thesis_never_monitored_has_no_snapshot()
    {
        var thesis = await SeedAsync(Guid.NewGuid());

        (await ReadAsync(thesis.Id)).MonitorSnapshot.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task The_write_is_scoped_to_the_named_user()
    {
        var mine = await SeedAsync(Guid.NewGuid());
        var theirs = await SeedAsync(Guid.NewGuid());

        await using (var db = CreateContext())
        {
            await new ThesisRepository(db).SaveMonitorSnapshotAsync(
                mine.UserId, theirs.Id, new ThesisMonitorSnapshot(RunAt, 1, ["k"]));
        }

        (await ReadAsync(theirs.Id)).MonitorSnapshot.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task An_upsert_of_the_thesis_keeps_the_stored_snapshot()
    {
        var thesis = await SeedAsync(Guid.NewGuid());
        await using (var db = CreateContext())
        {
            await new ThesisRepository(db).SaveMonitorSnapshotAsync(
                thesis.UserId, thesis.Id, new ThesisMonitorSnapshot(RunAt, 1, ["k"]));
        }

        await using (var db = CreateContext())
        {
            var loaded = await ReadAsync(thesis.Id);
            loaded.ThesisText = "edited";
            await new ThesisRepository(db).UpsertAsync(loaded);
        }

        var stored = await ReadAsync(thesis.Id);
        stored.ThesisText.Should().Be("edited");
        stored.MonitorSnapshot!.EvaluableKeys.Should().Equal("k");
    }
}
