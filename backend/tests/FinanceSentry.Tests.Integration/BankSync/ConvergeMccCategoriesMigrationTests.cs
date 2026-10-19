namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Categorization;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

/// <summary>M028 converges the four live MCC rows the insert-only seeder left UNCATEGORIZED, and the transactions still following them.</summary>
[Trait("Category", "Integration")]
public sealed class ConvergeMccCategoriesMigrationTests : IAsyncLifetime
{
    private const string BeforeMigration = "20261017120000_M027_AccountPreviousLast4";
    private const string Migration = "20261019120000_M028_ConvergeMccCategories";

    private TestDatabase? _database;

    public async Task InitializeAsync() => _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private BankSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            NoCurrentUser.Instance);

    [DockerRequiredFact]
    public async Task Migrate_ConvergesStaleRowsAndFollowingTransactions_AndLeavesEditedOnesAlone()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeMigration);

        CategorySeeder.Seed(db);
        // 4215 was hand-edited, so it must survive; the other three and 5832 are the stale live rows.
        await db.MccCategories.Where(m => new[] { 4214, 4225, 5832 }.Contains(m.Mcc))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CategoryKey, CategoryKeys.Uncategorized));
        await db.MccCategories.Where(m => m.Mcc == 4215)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CategoryKey, CategoryKeys.FoodAndDrink));

        var account = new BankAccount(userId, "ext-1", "Bank", "checking", "1234", "Owner", "UAH", userId, "monobank");
        Transaction Tx(int mcc, string category) =>
            new(account.Id, userId, -10m, DateTime.UtcNow, "x", Guid.NewGuid().ToString("N")) { Mcc = mcc, MerchantCategory = category };
        var stale = Tx(4214, CategoryKeys.Uncategorized);
        var stale5832 = Tx(5832, CategoryKeys.Uncategorized);
        var manual = Tx(4214, CategoryKeys.FoodAndDrink);
        var other = Tx(5812, CategoryKeys.Uncategorized);
        db.BankAccounts.Add(account);
        db.Transactions.AddRange(stale, stale5832, manual, other);
        await db.SaveChangesAsync();

        await migrator.MigrateAsync(Migration);
        await migrator.MigrateAsync(BeforeMigration);
        await migrator.MigrateAsync(Migration);

        await using var check = CreateContext();
        var mcc = await check.MccCategories.AsNoTracking().ToDictionaryAsync(m => m.Mcc, m => m.CategoryKey);
        mcc[4214].Should().Be(CategoryKeys.GeneralServices);
        mcc[4225].Should().Be(CategoryKeys.GeneralServices);
        mcc[5832].Should().Be(CategoryKeys.GeneralMerchandise);
        mcc[4215].Should().Be(CategoryKeys.FoodAndDrink, "a row that no longer says UNCATEGORIZED is not the migration's to touch");

        var txns = await check.Transactions.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.MerchantCategory);
        txns[stale.Id].Should().Be(CategoryKeys.GeneralServices);
        txns[stale5832.Id].Should().Be(CategoryKeys.GeneralMerchandise);
        txns[manual.Id].Should().Be(CategoryKeys.FoodAndDrink);
        txns[other.Id].Should().Be(CategoryKeys.Uncategorized);
    }
}
