namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The set-based transaction writes (<c>ExecuteUpdateAsync</c>): the 24-month retention archive and the
/// soft-delete of an account's transactions. Each asserts the rows it must touch and the rows it must leave
/// alone, in particular that a row already archived keeps its original timestamp and reason. Real Postgres,
/// because the InMemory provider cannot run a set-based update.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BankSyncBulkWriteTests : IAsyncLifetime
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
    private BankSyncDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static BankAccount NewAccount(Guid userId) =>
        new(userId, $"ext-{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", "EUR", userId, "monobank");

    private static Transaction NewTransaction(BankAccount account, DateTime? postedDate) =>
        new(account.Id, account.UserId, -10m, DateTime.UtcNow, "coffee", Guid.NewGuid().ToString("N"))
        {
            PostedDate = postedDate,
        };

    private async Task SeedAsync(params object[] entities)
    {
        await using var seed = CreateContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    private async Task<Transaction> ReloadAsync(Transaction transaction)
    {
        await using var read = CreateContext();
        return await read.Transactions
            .IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.ActiveFilterName]).AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
    }

    private static async Task RunRetentionAsync(BankSyncDbContext ctx, bool dryRun = false) =>
        await new DataRetentionJob(ctx, NullLogger<DataRetentionJob>.Instance).RunAsync(dryRun);

    [DockerRequiredFact]
    public async Task Retention_archives_only_active_transactions_posted_before_the_cutoff()
    {
        var account = NewAccount(_userA);
        var otherAccount = NewAccount(_userB);
        var old = NewTransaction(account, DateTime.UtcNow.AddMonths(-25));
        var otherUsersOld = NewTransaction(otherAccount, DateTime.UtcNow.AddMonths(-30));
        var recent = NewTransaction(account, DateTime.UtcNow.AddMonths(-23));
        var pending = NewTransaction(account, null);
        var archivedAt = DateTime.UtcNow.AddMonths(-5);
        var alreadyArchived = NewTransaction(account, DateTime.UtcNow.AddMonths(-26));
        alreadyArchived.IsActive = false;
        alreadyArchived.DeletedAt = archivedAt;
        alreadyArchived.ArchivedReason = "account_deleted";
        await SeedAsync(account, otherAccount, old, otherUsersOld, recent, pending, alreadyArchived);

        var before = DateTime.UtcNow;
        await using (var ctx = CreateContext())
            await RunRetentionAsync(ctx);

        foreach (var archived in new[] { old, otherUsersOld })
        {
            var row = await ReloadAsync(archived);
            row.IsActive.Should().BeFalse();
            row.DeletedAt.Should().BeOnOrAfter(before);
            row.ArchivedReason.Should().Be("retention_policy_24m");
        }

        foreach (var untouched in new[] { recent, pending })
        {
            var row = await ReloadAsync(untouched);
            row.IsActive.Should().BeTrue();
            row.DeletedAt.Should().BeNull();
            row.ArchivedReason.Should().BeNull();
        }

        var kept = await ReloadAsync(alreadyArchived);
        kept.IsActive.Should().BeFalse();
        kept.DeletedAt.Should().BeCloseTo(archivedAt, TimeSpan.FromSeconds(1));
        kept.ArchivedReason.Should().Be("account_deleted");
    }

    [DockerRequiredFact]
    public async Task Retention_dry_run_writes_nothing()
    {
        var account = NewAccount(_userA);
        var old = NewTransaction(account, DateTime.UtcNow.AddMonths(-25));
        await SeedAsync(account, old);

        await using (var ctx = CreateContext())
            await RunRetentionAsync(ctx, dryRun: true);

        var row = await ReloadAsync(old);
        row.IsActive.Should().BeTrue();
        row.DeletedAt.Should().BeNull();
        row.ArchivedReason.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task Retention_is_idempotent()
    {
        var account = NewAccount(_userA);
        var old = NewTransaction(account, DateTime.UtcNow.AddMonths(-25));
        await SeedAsync(account, old);

        await using (var ctx = CreateContext())
            await RunRetentionAsync(ctx);
        var first = await ReloadAsync(old);

        await using (var ctx = CreateContext())
            await RunRetentionAsync(ctx);

        var second = await ReloadAsync(old);
        second.DeletedAt.Should().Be(first.DeletedAt, "a second run must not re-archive an archived row");
    }

    [DockerRequiredFact]
    public async Task SoftDelete_by_account_archives_only_that_accounts_active_transactions()
    {
        var account = NewAccount(_userA);
        var otherAccount = NewAccount(_userA);
        var first = NewTransaction(account, DateTime.UtcNow.AddDays(-1));
        var second = NewTransaction(account, null);
        var otherAccountsRow = NewTransaction(otherAccount, DateTime.UtcNow.AddDays(-1));
        var archivedAt = DateTime.UtcNow.AddMonths(-30);
        var alreadyArchived = NewTransaction(account, DateTime.UtcNow.AddMonths(-26));
        alreadyArchived.IsActive = false;
        alreadyArchived.DeletedAt = archivedAt;
        alreadyArchived.ArchivedReason = "retention_policy_24m";
        await SeedAsync(account, otherAccount, first, second, otherAccountsRow, alreadyArchived);

        var before = DateTime.UtcNow;
        await using (var ctx = CreateContext(_userA))
            await new TransactionRepository(ctx).SoftDeleteByAccountIdAsync(account.Id);

        foreach (var archived in new[] { first, second })
        {
            var row = await ReloadAsync(archived);
            row.IsActive.Should().BeFalse();
            row.DeletedAt.Should().BeOnOrAfter(before);
            row.ArchivedReason.Should().Be("account_deleted");
        }

        var untouched = await ReloadAsync(otherAccountsRow);
        untouched.IsActive.Should().BeTrue();
        untouched.DeletedAt.Should().BeNull();
        untouched.ArchivedReason.Should().BeNull();

        var kept = await ReloadAsync(alreadyArchived);
        kept.DeletedAt.Should().BeCloseTo(archivedAt, TimeSpan.FromSeconds(1));
        kept.ArchivedReason.Should().Be("retention_policy_24m");
    }

    [DockerRequiredFact]
    public async Task SoftDelete_by_account_acting_for_another_person_archives_nothing()
    {
        var account = NewAccount(_userA);
        var row = NewTransaction(account, DateTime.UtcNow.AddDays(-1));
        await SeedAsync(account, row);

        await using (var ctx = CreateContext(_userB))
            await new TransactionRepository(ctx).SoftDeleteByAccountIdAsync(account.Id);

        (await ReloadAsync(row)).IsActive.Should().BeTrue();
    }
}
