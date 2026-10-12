namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The named <c>IsActive</c> query filter on <see cref="BankAccount"/>, next to the Owner filter. A removed
/// (soft-deleted) account drops out of every total and listing without a hand-written predicate; the paths
/// that must still see it (the duplicate check on reconnect, the claim release, the health write, the
/// removal flow itself) opt out of that one filter by name and stay bound by the Owner filter.
/// Real Postgres is required, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BankAccountActiveFilterTests : IAsyncLifetime
{
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _otherUser = Guid.NewGuid();
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

    private static BankAccount NewAccount(Guid userId, decimal balance, bool isActive = true, string syncStatus = "active") =>
        new(userId, $"ext-{Guid.NewGuid():N}", "Bank", "checking", "1234", "Owner", "USD", userId, "monobank")
        {
            CurrentBalance = balance,
            IsActive = isActive,
            SyncStatus = syncStatus,
        };

    private static Transaction NewTransaction(BankAccount account, decimal amount) =>
        new(account.Id, account.UserId, amount, DateTime.UtcNow, "coffee", Guid.NewGuid().ToString("N"))
        {
            PostedDate = DateTime.UtcNow,
            TransactionType = amount < 0 ? "debit" : "credit",
        };

    private async Task SeedAsync(params object[] entities)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    private async Task<BankAccount> ReloadAsync(Guid id)
    {
        await using var read = CreateContext();
        return await read.BankAccounts
            .IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.AccountActiveFilterName])
            .AsNoTracking().SingleAsync(a => a.Id == id);
    }

    [DockerRequiredFact]
    public void Account_declares_the_IsActive_filter_next_to_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var filters = ctx.Model.FindEntityType(typeof(BankAccount))!.GetDeclaredQueryFilters().Select(f => f.Key).ToList();

        filters.Should().Contain([OwnerQueryFilter.Name, BankSyncDbContext.AccountActiveFilterName]);
    }

    [DockerRequiredFact]
    public async Task Inactive_account_is_excluded_from_balances_counts_and_net_worth()
    {
        var active = NewAccount(_user, 100m);
        var removed = NewAccount(_user, 5_000m, isActive: false);
        await SeedAsync(active, removed);

        await using var ctx = CreateContext(_user);
        var aggregation = new AggregationService(new BankAccountRepository(ctx));

        (await aggregation.GetAggregatedBalanceAsync(_user)).Should().Equal(new Dictionary<string, decimal> { ["USD"] = 100m });
        (await aggregation.GetTotalNetWorthUsdAsync(_user)).Should().Be(100m);
        (await aggregation.GetAccountCountByTypeAsync(_user)).Should().Equal(new Dictionary<string, int> { ["checking"] = 1 });
    }

    [DockerRequiredFact]
    public async Task Inactive_account_is_excluded_from_the_no_person_readers()
    {
        var active = NewAccount(_user, 100m);
        var removed = NewAccount(_user, 5_000m, isActive: false);
        var onlyRemoved = NewAccount(_otherUser, 7m, isActive: false);
        await SeedAsync(active, removed, onlyRemoved);

        await using var ctx = CreateContext();
        var accounts = new BankAccountRepository(ctx);
        var totals = new BankingTotalsReader(accounts, new SyncJobRepository(ctx));

        (await totals.GetTotalUsdAsync(_user)).Should().Be(100m);
        (await totals.GetActiveUserIdsAsync()).Should().Contain(_user).And.NotContain(_otherUser);
        (await accounts.GetByUserIdUnscopedAsync(_user)).Select(a => a.Id).Should().Equal(active.Id);
        (await accounts.GetByIdUnscopedAsync(removed.Id)).Should().BeNull();
        (await accounts.GetByExternalAccountIdUnscopedAsync(removed.ExternalAccountId)).Should().BeNull();
        (await accounts.GetAllActiveUnscopedAsync()).Select(a => a.Id).Should().Contain(active.Id)
            .And.NotContain([removed.Id, onlyRemoved.Id]);
        (await ActiveAccountSnapshot.ReadAsync(ctx, default)).AccountIds.Should().Contain(active.Id)
            .And.NotContain([removed.Id, onlyRemoved.Id]);
    }

    [DockerRequiredFact]
    public async Task Person_scoped_reads_skip_an_inactive_account_and_stay_owner_bound()
    {
        var active = NewAccount(_user, 100m);
        var removed = NewAccount(_user, 5_000m, isActive: false);
        var others = NewAccount(_otherUser, 9m);
        await SeedAsync(active, removed, others);

        await using var ctx = CreateContext(_user);
        var accounts = new BankAccountRepository(ctx);

        (await accounts.GetByUserIdAsync(_user)).Select(a => a.Id).Should().Equal(active.Id);
        (await accounts.GetByIdAsync(removed.Id)).Should().BeNull();
        (await accounts.GetByIdAsync(others.Id)).Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task Transaction_reader_returns_only_the_requested_window_of_active_accounts()
    {
        var active = NewAccount(_user, 100m);
        var removed = NewAccount(_user, 5_000m, isActive: false);
        var inWindow = NewTransaction(active, -10m);
        var outOfWindow = NewTransaction(active, -20m);
        outOfWindow.PostedDate = DateTime.UtcNow.AddMonths(-3);
        var archived = NewTransaction(active, -30m);
        archived.IsActive = false;
        await SeedAsync(active, removed, inWindow, outOfWindow, archived);

        await using var ctx = CreateContext();
        var reader = new BankingTransactionReader(new BankAccountRepository(ctx), new TransactionRepository(ctx));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await reader.GetTransactionsAsync(_user, today.AddDays(-1), today);

        result.Should().ContainSingle().Which.Amount.Should().Be(-10m);
    }

    [DockerRequiredFact]
    public async Task Reconnect_duplicate_check_still_sees_a_removed_account_across_users()
    {
        var removed = NewAccount(_user, 5_000m, isActive: false);
        await SeedAsync(removed);

        await using var ctx = CreateContext();

        (await new BankAccountRepository(ctx).ExistsByExternalAccountIdUnscopedAsync(removed.ExternalAccountId))
            .Should().BeTrue("the unique external id is still occupied, so the account must not be inserted again");
    }

    [DockerRequiredFact]
    public async Task Reaper_release_and_health_write_still_reach_a_removed_account()
    {
        var removed = NewAccount(_user, 5_000m, isActive: false, syncStatus: "syncing");
        await SeedAsync(removed);

        await using (var ctx = CreateContext())
        {
            var accounts = new BankAccountRepository(ctx);

            await accounts.ReleaseSyncUnscopedAsync(removed.Id);
            await accounts.SaveHealthUnscopedAsync(removed.Id, new ConnectionHealth { State = ConnectionHealthState.Failing });
        }

        var reloaded = await ReloadAsync(removed.Id);
        reloaded.SyncStatus.Should().Be("active");
        reloaded.Health.State.Should().Be(ConnectionHealthState.Failing);
    }

    [DockerRequiredFact]
    public async Task Claim_and_stale_sync_reaper_leave_a_removed_account_alone()
    {
        var removed = NewAccount(_user, 5_000m, isActive: false, syncStatus: "syncing");
        await SeedAsync(removed);

        await using var ctx = CreateContext();
        var accounts = new BankAccountRepository(ctx);

        (await accounts.TryClaimSyncUnscopedAsync(removed.Id)).Should().BeFalse();
        await new StaleSyncReaperJob(new SyncJobRepository(ctx), accounts, Microsoft.Extensions.Logging.Abstractions.NullLogger<StaleSyncReaperJob>.Instance)
            .ExecuteAsync();

        (await ReloadAsync(removed.Id)).SyncStatus.Should().Be("syncing");
    }

    [DockerRequiredFact]
    public async Task Removal_is_idempotent_and_hard_delete_reaches_a_removed_account_but_only_the_owners()
    {
        var removed = NewAccount(_user, 5_000m, isActive: false);
        var toDelete = NewAccount(_user, 1m, isActive: false);
        await SeedAsync(removed, toDelete);

        await using (var asOther = CreateContext(_otherUser))
        {
            var accounts = new BankAccountRepository(asOther);
            (await accounts.DeleteAsync(removed.Id)).Should().BeFalse("opting out of IsActive keeps the Owner filter");
            (await accounts.HardDeleteAsync(toDelete.Id)).Should().BeFalse();
        }

        await using (var asOwner = CreateContext(_user))
        {
            var accounts = new BankAccountRepository(asOwner);
            (await accounts.DeleteAsync(removed.Id)).Should().BeTrue();
            (await accounts.HardDeleteAsync(toDelete.Id)).Should().BeTrue();
        }

        (await ReloadAsync(removed.Id)).IsActive.Should().BeFalse();
        await using var read = CreateContext();
        (await read.BankAccounts.IgnoreQueryFilters([OwnerQueryFilter.Name, BankSyncDbContext.AccountActiveFilterName])
            .AnyAsync(a => a.Id == toDelete.Id)).Should().BeFalse();
    }
}
