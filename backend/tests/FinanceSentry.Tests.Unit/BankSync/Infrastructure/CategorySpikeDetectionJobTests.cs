namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="CategorySpikeDetectionJob"/> (044/US3).
/// Verifies the 4-month minimum history gate, 6-month baseline averaging, configurable multiplier,
/// and USD-normalised comparison.
/// </summary>
public class CategorySpikeDetectionJobTests
{
    private readonly Mock<IAlertGeneratorService> _alerts = new();

    private static BankSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<BankSyncDbContext>()
            .UseInMemoryDatabase($"catspike-{Guid.NewGuid():N}").Options);

    private static IConfiguration DefaultConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithMultiplier(decimal multiplier) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HygieneSentinels:CategorySpikeMultiplier"] = multiplier.ToString("F4"),
            })
            .Build();

    private CategorySpikeDetectionJob MakeJob(BankSyncDbContext db, IConfiguration? config = null) =>
        new(db, _alerts.Object, config ?? DefaultConfig(),
            Mock.Of<ILogger<CategorySpikeDetectionJob>>());

    private static BankAccount MakeAccount(Guid userId, string currency = "EUR")
    {
        var account = new BankAccount(userId, Guid.NewGuid().ToString(), "Test Bank",
            "current", "1234", "Owner", currency, Guid.NewGuid(), "monobank");
        return account;
    }

    /// <summary>
    /// Production shape: adapters persist a positive <see cref="Transaction.Amount"/> and carry the
    /// direction in <see cref="Transaction.TransactionType"/> (a negative amount fails
    /// <see cref="Transaction.ValidateInvariants"/>).
    /// </summary>
    private static Transaction MakeTx(BankAccount account, decimal amount, string category, DateTime date,
        string transactionType = "debit")
    {
        var tx = new Transaction(account.Id, account.UserId, Math.Abs(amount), date, "desc",
            Guid.NewGuid().ToString())
        {
            MerchantCategory = category,
            TransactionType = transactionType,
            IsActive = true,
        };
        tx.ValidateInvariants();
        return tx;
    }

    private static Transaction MakePendingTx(BankAccount account, decimal amount, string category,
        DateTime date)
    {
        var tx = MakeTx(account, amount, category, date);
        tx.IsPending = true;
        return tx;
    }

    /// <summary>
    /// Signed-negative shape: no ingest path can produce it (<see cref="Transaction.ValidateInvariants"/>
    /// rejects it), so this covers the predicate's defensive sign arm only — a row written outside the
    /// repository still reads as spend.
    /// </summary>
    private static Transaction MakeSignedNegativeTx(BankAccount account, decimal amount, string category,
        DateTime date)
    {
        var tx = new Transaction(account.Id, account.UserId, -Math.Abs(amount), date, "desc",
            Guid.NewGuid().ToString())
        {
            MerchantCategory = category,
            IsActive = true,
        };
        return tx;
    }

    /// <summary>
    /// Builds 6 months of historic spend + a current-month spike and expects an alert.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlertFired_WhenCurrentMonthExceedsBaselineByMultiplier()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // 6 months of 100 EUR spend each
        for (var i = 1; i <= 6; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "FOOD_AND_DRINK",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }

        // Current month: 300 EUR — 3× the baseline, well above the default 2.0× threshold
        db.Transactions.Add(MakeTx(account, 300m, "FOOD_AND_DRINK", currentMonthStart.AddDays(5)));

        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            userId, "FOOD_AND_DRINK", It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenCurrentMonthBelowThreshold()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 6; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "TRANSPORT",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }
        // Current month: 110 EUR — 10% above baseline, well below the 2.0× default
        db.Transactions.Add(MakeTx(account, 110m, "TRANSPORT", currentMonthStart.AddDays(5)));

        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenInsufficientHistory()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Only 3 months of history — below the 4-month minimum requirement
        for (var i = 1; i <= 3; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "SHOPPING",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }
        db.Transactions.Add(MakeTx(account, 500m, "SHOPPING", currentMonthStart.AddDays(5)));

        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AlertFired_WhenExactlyMinimumHistoryMonths()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Exactly 4 months of history — meets the minimum; current-month spike should fire.
        for (var i = 1; i <= 4; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "SHOPPING",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }
        // 400 EUR — 4× the 100 EUR baseline, comfortably above the 2.0× threshold
        db.Transactions.Add(MakeTx(account, 400m, "SHOPPING", currentMonthStart.AddDays(5)));

        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            userId, "SHOPPING", It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfigurableMultiplier()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 6; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "ENTERTAINMENT",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }
        // 120 EUR — 20% above baseline: above 1.1× custom threshold, below 2.0× default
        db.Transactions.Add(MakeTx(account, 120m, "ENTERTAINMENT", currentMonthStart.AddDays(5)));

        await db.SaveChangesAsync();

        await MakeJob(db, ConfigWithMultiplier(1.1m)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            userId, "ENTERTAINMENT", It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenNoCurrentMonthSpend()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // 6 months of history but no current-month spend
        for (var i = 1; i <= 6; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "HEALTH",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }

        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Direction comes from <see cref="Transaction.TransactionType"/>, not the sign: a credit
    /// (refund, salary) carries the same positive amount as a debit and must never count as spend.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenCurrentMonthInflowIsACredit()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 6; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "SHOPPING",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }
        // Current month holds real (sub-threshold) spend plus a 999 EUR credit. Counting the credit
        // would push the month to 10× the baseline and fire.
        db.Transactions.Add(MakeTx(account, 50m, "SHOPPING", currentMonthStart.AddDays(3)));
        db.Transactions.Add(MakeTx(account, 999m, "SHOPPING", currentMonthStart.AddDays(5),
            transactionType: "credit"));

        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Pins the shape the adapters actually persist (positive amount + <c>TransactionType</c>):
    /// a filter keyed on a negative sign alone matches nothing in production and the sentinel
    /// would never fire.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlertFired_ForPositiveAmountDebits()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        // USD account: UpdateRates pins USD at 1.00, so the asserted totals are exact.
        var account = MakeAccount(userId, "USD");
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 6; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "TRANSPORT",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }
        db.Transactions.Add(MakeTx(account, 300m, "TRANSPORT", currentMonthStart.AddDays(5)));

        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            userId, "TRANSPORT", 300m, 100m, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// A pending charge is kept alongside the posted row it later becomes (they hash differently),
    /// so counting both would double the month's spend and fire a phantom spike.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenPendingDuplicatesThePostedCharge()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 6; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "FOOD_AND_DRINK",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }
        // 150 posted + its 150 pending twin. Counting both reaches 300 — 3× baseline — and fires.
        db.Transactions.Add(MakeTx(account, 150m, "FOOD_AND_DRINK", currentMonthStart.AddDays(4)));
        db.Transactions.Add(MakePendingTx(account, 150m, "FOOD_AND_DRINK", currentMonthStart.AddDays(5)));

        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The predicate's defensive sign arm: a signed-negative row still reads as spend.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlertFired_ForLegacySignedNegativeRows()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        // USD account: UpdateRates pins USD at 1.00, so the asserted totals are exact.
        var account = MakeAccount(userId, "USD");
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 6; i++)
        {
            db.Transactions.Add(MakeSignedNegativeTx(account, 100m, "HEALTH",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }
        db.Transactions.Add(MakeSignedNegativeTx(account, 300m, "HEALTH", currentMonthStart.AddDays(5)));

        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            userId, "HEALTH", 300m, 100m, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Liveness policy shared by the 044 sentinels: a disconnected (inactive) account's
    /// transactions must not raise new alerts.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenAccountIsInactive()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        account.IsActive = false;
        db.BankAccounts.Add(account);

        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= 6; i++)
        {
            db.Transactions.Add(MakeTx(account, 100m, "FOOD_AND_DRINK",
                currentMonthStart.AddMonths(-i).AddDays(5)));
        }
        db.Transactions.Add(MakeTx(account, 300m, "FOOD_AND_DRINK", currentMonthStart.AddDays(5)));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateCategorySpikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
