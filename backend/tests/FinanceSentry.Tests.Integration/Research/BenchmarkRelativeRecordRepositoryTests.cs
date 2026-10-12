namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Stores benchmark-relative runs (fs-699) in a real PostgreSQL instance. A second run on the same
/// day replaces the first under the unique (user, as-of, scope, key, window) index. The in-memory
/// provider does not enforce that index, so only a real database can catch a delete/insert
/// ordering bug there.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BenchmarkRelativeRecordRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextMonday = Monday.AddDays(7);

    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // Null acting user is no person in scope, as in a job.
    private ResearchDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<ResearchDbContext>()
            .UseNpgsql(_database!.ConnectionString)
            .Options, new FixedCurrentUser(actingUser));

    private static BenchmarkRelativeRecord Book(Guid userId, DateTimeOffset asOf, decimal excess) => new()
    {
        UserId = userId,
        AsOf = asOf,
        Scope = TrackRecordScope.Book,
        ScopeKey = "book",
        Label = "Book",
        Window = TrackRecordWindow.ThreeMonths,
        Covered = true,
        ExcessReturnPct = excess,
        NetGate = NetExcessGate.NotHeld,
    };

    [DockerRequiredFact]
    public async Task ReplaceRun_WhenTheInsertFails_KeepsTheExistingRun()
    {
        await using (var setup = CreateContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var userId = Guid.NewGuid();

        await using (var ctx = CreateContext())
        {
            await new BenchmarkRelativeRecordRepository(ctx).ReplaceRunAsync(userId, Monday, [Book(userId, Monday, -6m)]);
        }

        // Two rows with the same (user, as-of, scope, key, window) violate the unique index on insert.
        await using (var ctx = CreateContext())
        {
            var act = () => new BenchmarkRelativeRecordRepository(ctx).ReplaceRunAsync(
                userId, Monday, [Book(userId, Monday, -1m), Book(userId, Monday, -2m)]);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var ctx = CreateContext(userId))
        {
            var latest = await new BenchmarkRelativeRecordRepository(ctx).ListLatestRunAsync(userId);
            latest.Should().ContainSingle().Which.ExcessReturnPct.Should().Be(-6m);
        }
    }

    [DockerRequiredFact]
    public async Task SameDayRerun_ReplacesTheRun_AndPreviousRunReadsTheEarlierDay()
    {
        await using (var setup = CreateContext())
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var userId = Guid.NewGuid();

        await using (var ctx = CreateContext())
        {
            var repo = new BenchmarkRelativeRecordRepository(ctx);
            await repo.ReplaceRunAsync(userId, Monday, [Book(userId, Monday, -6m)]);
            await repo.ReplaceRunAsync(userId, NextMonday, [Book(userId, NextMonday, -7m)]);
        }

        await using (var ctx = CreateContext())
        {
            var repo = new BenchmarkRelativeRecordRepository(ctx);
            await repo.ReplaceRunAsync(userId, NextMonday, [Book(userId, NextMonday, -8.1234m)]);
        }

        await using (var ctx = CreateContext(userId))
        {
            var repo = new BenchmarkRelativeRecordRepository(ctx);

            var latest = await repo.ListLatestRunAsync(userId);
            latest.Should().ContainSingle().Which.ExcessReturnPct.Should().Be(-8.1234m);

            var previous = await repo.ListPreviousRunUnscopedAsync(userId, NextMonday);
            previous.Should().ContainSingle().Which.AsOf.Should().Be(Monday);

            (await repo.ListLatestRunAsync(Guid.NewGuid())).Should().BeEmpty();
        }
    }
}
