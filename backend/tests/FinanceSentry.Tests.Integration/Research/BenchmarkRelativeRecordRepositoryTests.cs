namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
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

    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    // Null acting user is no person in scope, as in a job.
    private ResearchDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<ResearchDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString())
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
