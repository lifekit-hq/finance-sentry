namespace FinanceSentry.Tests.Integration.BankSync;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Exercises <see cref="TransactionRepository.GetFilteredByUserIdAsync"/> and
/// <see cref="TransactionRepository.GetFilteredByAccountIdAsync"/> against real Postgres: the
/// COALESCE date predicate, ILIKE search, and the amount-range Union all need real SQL translation
/// that the in-memory provider does not exercise faithfully.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TransactionFilterQueryTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private BankSyncDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString())
            .Options);

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static BankAccount NewAccount(Guid userId, string currency = "EUR") =>
        new(userId, Guid.NewGuid().ToString("N")[..12], "Test Bank", "checking", "1234",
            "Test Owner", currency, userId, "test-provider");

    private static Transaction NewTransaction(
        Guid accountId, Guid userId, decimal amount, DateTime transactionDate,
        string description, string uniqueHash,
        DateTime? postedDate = null, string? transactionType = null,
        string? merchantCategory = null, string? merchantName = null) =>
        new(accountId, userId, amount, transactionDate, description, uniqueHash)
        {
            PostedDate = postedDate,
            TransactionType = transactionType,
            MerchantCategory = merchantCategory,
            MerchantName = merchantName,
        };

    [DockerRequiredFact]
    public async Task GetFilteredByUserIdAsync_WithNoFilters_ReturnsAllUserTransactions_OrderedByDateDescending()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var account = NewAccount(userId);
        var older = NewTransaction(account.Id, userId, 10m, Utc(2026, 1, 1), "Older", "hash-older");
        var newer = NewTransaction(account.Id, userId, 20m, Utc(2026, 2, 1), "Newer", "hash-newer");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.Add(account);
            seed.Transactions.AddRange(older, newer);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByUserIdAsync(userId, new TransactionFilter(), 0, 50);

        totalCount.Should().Be(2);
        items.Select(t => t.Description).Should().ContainInOrder("Newer", "Older");
    }

    [DockerRequiredFact]
    public async Task GetFilteredByUserIdAsync_FiltersByAccountId()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var accountA = NewAccount(userId);
        var accountB = NewAccount(userId);
        var inA = NewTransaction(accountA.Id, userId, 10m, DateTime.UtcNow, "In A", "hash-a");
        var inB = NewTransaction(accountB.Id, userId, 10m, DateTime.UtcNow, "In B", "hash-b");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.AddRange(accountA, accountB);
            seed.Transactions.AddRange(inA, inB);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByUserIdAsync(
            userId, new TransactionFilter(AccountIds: [accountA.Id]), 0, 50);

        totalCount.Should().Be(1);
        items.Single().Description.Should().Be("In A");
    }

    [DockerRequiredFact]
    public async Task GetFilteredByUserIdAsync_FiltersByCategory()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var account = NewAccount(userId);
        var food = NewTransaction(account.Id, userId, 10m, DateTime.UtcNow, "Food", "hash-food", merchantCategory: "FOOD_AND_DRINK");
        var travel = NewTransaction(account.Id, userId, 10m, DateTime.UtcNow, "Travel", "hash-travel", merchantCategory: "TRAVEL");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.Add(account);
            seed.Transactions.AddRange(food, travel);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByUserIdAsync(
            userId, new TransactionFilter(Categories: ["FOOD_AND_DRINK"]), 0, 50);

        totalCount.Should().Be(1);
        items.Single().Description.Should().Be("Food");
    }

    [DockerRequiredFact]
    public async Task GetFilteredByUserIdAsync_FiltersByDateRange_UsingPostedDateFallingBackToTransactionDate()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var account = NewAccount(userId);
        var inRange = NewTransaction(account.Id, userId, 10m, Utc(2026, 3, 15), "In range", "hash-in",
            postedDate: Utc(2026, 3, 16));
        var outOfRange = NewTransaction(account.Id, userId, 10m, Utc(2026, 1, 1), "Out of range", "hash-out");
        // Pending transaction with no PostedDate: must fall back to TransactionDate for the range check.
        var pendingInRange = NewTransaction(account.Id, userId, 10m, Utc(2026, 3, 18), "Pending in range", "hash-pending");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.Add(account);
            seed.Transactions.AddRange(inRange, outOfRange, pendingInRange);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByUserIdAsync(
            userId, new TransactionFilter(From: Utc(2026, 3, 1), To: Utc(2026, 3, 31)), 0, 50);

        totalCount.Should().Be(2);
        items.Select(t => t.Description).Should().BeEquivalentTo(["In range", "Pending in range"]);
    }

    [DockerRequiredFact]
    public async Task GetFilteredByUserIdAsync_FiltersByTransactionType()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var account = NewAccount(userId);
        var debit = NewTransaction(account.Id, userId, 10m, DateTime.UtcNow, "Debit", "hash-debit", transactionType: "debit");
        var credit = NewTransaction(account.Id, userId, 10m, DateTime.UtcNow, "Credit", "hash-credit", transactionType: "credit");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.Add(account);
            seed.Transactions.AddRange(debit, credit);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByUserIdAsync(
            userId, new TransactionFilter(TransactionType: "credit"), 0, 50);

        totalCount.Should().Be(1);
        items.Single().Description.Should().Be("Credit");
    }

    [DockerRequiredFact]
    public async Task GetFilteredByUserIdAsync_FiltersBySearch_AcrossDescriptionAndMerchantName()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var account = NewAccount(userId);
        var byDescription = NewTransaction(account.Id, userId, 10m, DateTime.UtcNow, "Coffee at Blue Bottle", "hash-desc");
        var byMerchant = NewTransaction(account.Id, userId, 10m, DateTime.UtcNow, "Purchase", "hash-merch", merchantName: "Blue Bottle Coffee");
        var noMatch = NewTransaction(account.Id, userId, 10m, DateTime.UtcNow, "Groceries", "hash-none");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.Add(account);
            seed.Transactions.AddRange(byDescription, byMerchant, noMatch);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByUserIdAsync(
            userId, new TransactionFilter(Search: "blue bottle"), 0, 50);

        totalCount.Should().Be(2);
        items.Select(t => t.Description).Should().BeEquivalentTo(["Coffee at Blue Bottle", "Purchase"]);
    }

    [DockerRequiredFact]
    public async Task GetFilteredByAccountIdAsync_FiltersByNativeMinAndMaxAmount()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var account = NewAccount(userId);
        var small = NewTransaction(account.Id, userId, 5m, DateTime.UtcNow, "Small", "hash-small");
        var mid = NewTransaction(account.Id, userId, 50m, DateTime.UtcNow, "Mid", "hash-mid");
        var large = NewTransaction(account.Id, userId, 500m, DateTime.UtcNow, "Large", "hash-large");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.Add(account);
            seed.Transactions.AddRange(small, mid, large);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByAccountIdAsync(
            account.Id, new TransactionFilter(MinAmount: 10m, MaxAmount: 100m), 0, 50);

        totalCount.Should().Be(1);
        items.Single().Description.Should().Be("Mid");
    }

    [DockerRequiredFact]
    public async Task GetFilteredByUserIdAsync_FiltersByAmountRanges_OnePerCurrencyGroup()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var usdAccount = NewAccount(userId, "USD");
        var eurAccount = NewAccount(userId, "EUR");
        // USD group bound: 10-100. EUR group bound: 200-2000 (simulating a USD-normalised bound
        // translated to two different native ranges per currency).
        var usdInRange = NewTransaction(usdAccount.Id, userId, 50m, DateTime.UtcNow, "USD in range", "hash-usd-in");
        var usdOutOfRange = NewTransaction(usdAccount.Id, userId, 500m, DateTime.UtcNow, "USD out of range", "hash-usd-out");
        var eurInRange = NewTransaction(eurAccount.Id, userId, 500m, DateTime.UtcNow, "EUR in range", "hash-eur-in");
        var eurOutOfRange = NewTransaction(eurAccount.Id, userId, 5m, DateTime.UtcNow, "EUR out of range", "hash-eur-out");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.AddRange(usdAccount, eurAccount);
            seed.Transactions.AddRange(usdInRange, usdOutOfRange, eurInRange, eurOutOfRange);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByUserIdAsync(
            userId,
            new TransactionFilter(AmountRanges:
            [
                new AccountAmountRange([usdAccount.Id], 10m, 100m),
                new AccountAmountRange([eurAccount.Id], 200m, 2000m),
            ]),
            0, 50);

        totalCount.Should().Be(2);
        items.Select(t => t.Description).Should().BeEquivalentTo(["USD in range", "EUR in range"]);
    }

    [DockerRequiredFact]
    public async Task GetFilteredByUserIdAsync_CombinedFilters_TotalCountReflectsFilteredSetNotWholeTable()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var account = NewAccount(userId);
        var matches = NewTransaction(account.Id, userId, 50m, DateTime.UtcNow, "Matches", "hash-match",
            transactionType: "debit", merchantCategory: "FOOD_AND_DRINK");
        var wrongType = NewTransaction(account.Id, userId, 50m, DateTime.UtcNow, "Wrong type", "hash-wrong-type",
            transactionType: "credit", merchantCategory: "FOOD_AND_DRINK");
        var wrongCategory = NewTransaction(account.Id, userId, 50m, DateTime.UtcNow, "Wrong category", "hash-wrong-cat",
            transactionType: "debit", merchantCategory: "TRAVEL");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.Add(account);
            seed.Transactions.AddRange(matches, wrongType, wrongCategory);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByUserIdAsync(
            userId,
            new TransactionFilter(TransactionType: "debit", Categories: ["FOOD_AND_DRINK"]),
            0, 50);

        totalCount.Should().Be(1);
        items.Single().Description.Should().Be("Matches");
    }

    [DockerRequiredFact]
    public async Task GetFilteredByAccountIdAsync_WithNoFilters_ReproducesUnfilteredRead()
    {
        await using (var setup = CreateContext())
            await setup.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var account = NewAccount(userId);
        var first = NewTransaction(account.Id, userId, 10m, Utc(2026, 1, 1), "First", "hash-first");
        var second = NewTransaction(account.Id, userId, 20m, Utc(2026, 2, 1), "Second", "hash-second");

        await using (var seed = CreateContext())
        {
            seed.BankAccounts.Add(account);
            seed.Transactions.AddRange(first, second);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var repo = new TransactionRepository(ctx);
        var (items, totalCount) = await repo.GetFilteredByAccountIdAsync(account.Id, new TransactionFilter(), 0, 50);

        totalCount.Should().Be(2);
        items.Select(t => t.Description).Should().ContainInOrder("Second", "First");
    }
}
