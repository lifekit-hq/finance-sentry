namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The coverage sentinel against a real Postgres. The unit tests run on the in-memory provider, which evaluates every
/// predicate client-side and so cannot see a query EF Core fails to translate to SQL: this one runs the job's queries
/// as the relational provider compiles them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CoverageSentinelJobPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Today = new(2026, 9, 22, 4, 0, 0, TimeSpan.Zero);

    private TestDatabase? _database;

    public async Task InitializeAsync() => _database = await PostgresServer.Postgres14.ReserveDatabaseAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task Revolut_shape_runs_against_postgres_and_stores_the_hole_and_raises_the_alert()
    {
        await using var db = new BankSyncDbContext(
            new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            NoCurrentUser.Instance);
        await db.Database.MigrateAsync();

        var userId = Guid.NewGuid();
        var account = new BankAccount(userId, Guid.NewGuid().ToString(), "Revolut", "current", "3725", "Owner", "USD",
            Guid.NewGuid(), "truelayer");
        db.BankAccounts.Add(account);
        db.SyncJobs.Add(new SyncJob(account.Id, userId)
        {
            Status = "success",
            StartedAt = Today.UtcDateTime.AddHours(-3),
            CompletedAt = Today.UtcDateTime.AddHours(-3),
        });
        // $25 a day from 2026-05-04 through Sunday 2026-09-06, then nothing: the first empty week is 09-07.
        for (var d = new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc); d <= new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc); d = d.AddDays(1))
        {
            var tx = new Transaction(account.Id, userId, 25m, d.AddHours(12), "card", Guid.NewGuid().ToString())
            {
                MerchantCategory = "FOOD_AND_DRINK",
                TransactionType = "debit",
                IsActive = true,
            };
            tx.ValidateInvariants();
            db.Transactions.Add(tx);
        }
        await db.SaveChangesAsync();

        var alerts = new Mock<IAlertGeneratorService>();
        var closedAccounts = new Mock<IClosedAccountsReader>();
        closedAccounts.Setup(c => c.GetClosedUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());
        var job = new CoverageSentinelJob(db, closedAccounts.Object, alerts.Object, new FixedTimeProvider(Today),
            NullLogger<CoverageSentinelJob>.Instance);

        await job.ExecuteAsync();

        alerts.Verify(a => a.GenerateCoverageGapAlertAsync(
            userId, account.Id, "Revolut ···3725", 2, It.Is<decimal>(b => b > 100m), It.IsAny<CancellationToken>()), Times.Once);
        var hole = await db.CoverageHoles.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync();
        Assert.Equal(account.Id, hole.AccountId);
        Assert.Equal(new DateOnly(2026, 9, 7), hole.From);
        Assert.Null(hole.To);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
