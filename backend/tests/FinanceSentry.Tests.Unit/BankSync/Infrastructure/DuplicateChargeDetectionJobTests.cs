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
/// Unit tests for <see cref="DuplicateChargeDetectionJob"/> (044/US2).
/// Verifies the duplicate window, per-account scoping, and alert dispatch.
/// Dedup logic is AlertGeneratorService's responsibility and is not tested here.
/// </summary>
public class DuplicateChargeDetectionJobTests
{
    private readonly Mock<IAlertGeneratorService> _alerts = new();

    private static BankSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<BankSyncDbContext>()
            .UseInMemoryDatabase($"dup-{Guid.NewGuid():N}").Options);

    private static IConfiguration DefaultConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithWindow(int days) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HygieneSentinels:DuplicateWindowDays"] = days.ToString(),
            })
            .Build();

    private DuplicateChargeDetectionJob MakeJob(BankSyncDbContext db, IConfiguration? config = null) =>
        new(db, _alerts.Object, config ?? DefaultConfig(),
            Mock.Of<ILogger<DuplicateChargeDetectionJob>>());

    private static BankAccount MakeAccount(Guid userId, string currency = "EUR")
    {
        var account = new BankAccount(userId, Guid.NewGuid().ToString(), "Test Bank",
            "current", "1234", "Owner", currency, Guid.NewGuid(), "monobank");
        return account;
    }

    private static Transaction MakeTx(BankAccount account, decimal amount, string merchant,
        DateTime? date = null)
    {
        var tx = new Transaction(account.Id, account.UserId, amount,
            date ?? DateTime.UtcNow, merchant, Guid.NewGuid().ToString());
        tx.MerchantName = merchant;
        tx.IsActive = true;
        return tx;
    }

    [Fact]
    public async Task ExecuteAsync_AlertFired_WhenSameMerchantAndAmountTwiceWithinWindow()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);
        db.Transactions.AddRange(
            MakeTx(account, -9.99m, "Netflix"),
            MakeTx(account, -9.99m, "Netflix"));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            userId, account.Id, "netflix", "Netflix", 9.99m, "EUR", 2, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenOnlyOneOccurrence()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);
        db.Transactions.Add(MakeTx(account, -9.99m, "Spotify"));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenSameMerchantButDifferentAmounts()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);
        db.Transactions.AddRange(
            MakeTx(account, -9.99m, "Amazon"),
            MakeTx(account, -19.99m, "Amazon"));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenTransactionOutsideWindow()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);
        // One charge inside the window, one 10 days ago (outside default 5-day window)
        db.Transactions.AddRange(
            MakeTx(account, -9.99m, "Netflix", DateTime.UtcNow),
            MakeTx(account, -9.99m, "Netflix", DateTime.UtcNow.AddDays(-10)));
        await db.SaveChangesAsync();

        await MakeJob(db, ConfigWithWindow(5)).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ScopedPerAccount_DifferentAccountsDontMerge()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var accountA = MakeAccount(userId);
        var accountB = MakeAccount(userId);
        db.BankAccounts.AddRange(accountA, accountB);
        // One charge per account — should not be flagged (different accounts)
        db.Transactions.AddRange(
            MakeTx(accountA, -9.99m, "Netflix"),
            MakeTx(accountB, -9.99m, "Netflix"));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CountIsCorrect_WhenThreeDuplicates()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);
        db.Transactions.AddRange(
            MakeTx(account, -5.00m, "Gym"),
            MakeTx(account, -5.00m, "Gym"),
            MakeTx(account, -5.00m, "Gym"));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            userId, account.Id, "gym", "Gym", 5.00m, "EUR", 3, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenDuplicateTransactionsArePendingOrInactive()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);

        // One real active charge, one pending (not yet posted), one soft-deleted — only 1 active non-pending
        var active = MakeTx(account, -9.99m, "Netflix");
        var pending = MakeTx(account, -9.99m, "Netflix");
        pending.IsPending = true;
        var inactive = MakeTx(account, -9.99m, "Netflix");
        inactive.IsActive = false;
        db.Transactions.AddRange(active, pending, inactive);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A charge and its refund at the same merchant/amount are a round-trip, not a duplicate:
    /// grouping must be debit-only. Refund modeled per adapter convention (positive amount,
    /// TransactionType "credit").
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenChargeAndRefundShareMerchantAndAmount()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);

        var charge = MakeTx(account, -9.99m, "Netflix");
        var refund = MakeTx(account, 9.99m, "Netflix");
        refund.TransactionType = "credit";
        db.Transactions.AddRange(charge, refund);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Adapter convention stores amounts positive with the direction in TransactionType —
    /// two typed debits must still be detected as duplicates.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlertFired_WhenTwoTypedDebitChargesShareMerchantAndAmount()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);

        var first = MakeTx(account, 9.99m, "Netflix");
        first.TransactionType = "debit";
        var second = MakeTx(account, 9.99m, "Netflix");
        second.TransactionType = "debit";
        db.Transactions.AddRange(first, second);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            userId, account.Id, "netflix", "Netflix", 9.99m, "EUR", 2, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Spec US2 groups on the NORMALIZED merchant: the same merchant differing only in
    /// case/whitespace across two charges is one duplicate group.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlertFired_WhenMerchantNamesDifferOnlyInCaseAndWhitespace()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);
        db.Transactions.AddRange(
            MakeTx(account, -9.99m, "Netflix"),
            MakeTx(account, -9.99m, "  NETFLIX  "));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        // The display name is deliberately unpinned: the two spellings tie, so which one the alert
        // shows is not a property this test owns (ExecuteAsync_AlertCarriesRawStatementName does).
        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            userId, account.Id, "netflix", It.IsAny<string>(), 9.99m, "EUR", 2,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Every unnameable merchant normalizes to one sentinel key, so two unrelated charges that merely
    /// share an amount would land in the same group. That is not a duplicate — nothing is claimed.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenMerchantNamesAreBlank()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        db.BankAccounts.Add(account);

        var first = MakeTx(account, -9.99m, "Corner shop");
        first.MerchantName = "   ";
        var second = MakeTx(account, -9.99m, "Taxi ride");
        second.MerchantName = string.Empty;
        db.Transactions.AddRange(first, second);
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The normalized key is a grouping device, not something a user recognises: the alert has to
    /// name the merchant the way the statement spelled it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlertCarriesRawStatementName_NotTheNormalizedKey()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);
        db.Transactions.AddRange(
            MakeTx(account, -9.99m, "PAYPAL*Netflix.com 1234"),
            MakeTx(account, -9.99m, "PAYPAL*Netflix.com 1234"));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            userId, account.Id, "netflix", "PAYPAL*Netflix.com 1234", 9.99m, "EUR", 2,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenAccountIsInactive()
    {
        await using var db = NewDb();
        var account = MakeAccount(Guid.NewGuid());
        account.IsActive = false;
        db.BankAccounts.Add(account);
        db.Transactions.AddRange(
            MakeTx(account, -9.99m, "Netflix"),
            MakeTx(account, -9.99m, "Netflix"));
        await db.SaveChangesAsync();

        await MakeJob(db).ExecuteAsync();

        _alerts.Verify(a => a.GenerateDuplicateChargeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesOtherGroups_WhenOneAlertThrows()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var account = MakeAccount(userId);
        db.BankAccounts.Add(account);
        db.Transactions.AddRange(
            MakeTx(account, -9.99m, "Netflix"),
            MakeTx(account, -9.99m, "Netflix"),
            MakeTx(account, -5.00m, "Gym"),
            MakeTx(account, -5.00m, "Gym"));
        await db.SaveChangesAsync();

        var callCount = 0;
        _alerts.Setup(a => a.GenerateDuplicateChargeAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++callCount == 1) throw new InvalidOperationException("db failure");
                return Task.CompletedTask;
            });

        await MakeJob(db).ExecuteAsync();

        Assert.Equal(2, callCount);
    }
}
