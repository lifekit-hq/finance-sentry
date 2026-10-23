namespace FinanceSentry.Tests.Unit.BankSync.Maintenance;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Maintenance;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class RetiredPendingRepairServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Day = new(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RetiredAt = RetiredPendingRepairService.RetiredSince.AddDays(10);

    private readonly BankSyncDbContext _db = new(
        new DbContextOptionsBuilder<BankSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private readonly Mock<IProviderHistoryFetcher> _fetcher = new();
    private readonly Mock<ITransactionDeduplicationService> _dedup = new();

    public RetiredPendingRepairServiceTests()
    {
        _fetcher.Setup(f => f.FetchAsync(It.IsAny<BankAccount>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        // The hash is just the identifying text, so a fixture row's UniqueHash can be aimed at a candidate.
        _dedup.Setup(d => d.ComputeHash(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<string>()))
              .Returns((Guid _, decimal _, DateTime _, string description) => $"hash:{description}");
    }

    private RetiredPendingRepairService Sut() => new(_db, _fetcher.Object, _dedup.Object, NullLogger<RetiredPendingRepairService>.Instance);

    private async Task<BankAccount> AccountAsync(bool isActive = true)
    {
        var account = new BankAccount(UserId, Guid.NewGuid().ToString("N"), "Bank", "checking", "1234", "Owner", "EUR", UserId, "monobank")
        {
            IsActive = isActive,
        };
        _db.BankAccounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    private async Task<Transaction> RetiredAsync(
        BankAccount account, string description = "Rent", DateTime? retiredAt = null, string? archivedReason = null, decimal amount = -900m)
    {
        var row = new Transaction(account.Id, UserId, amount, Day, description, $"hash:{description}", isPending: true)
        {
            IsActive = false,
            DeletedAt = retiredAt ?? RetiredAt,
            ArchivedReason = archivedReason,
            TransactionType = "debit",
        };
        _db.Transactions.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private Task<Transaction> Reload(Transaction row) =>
        _db.Transactions.IgnoreQueryFilters().SingleAsync(t => t.Id == row.Id);

    [Fact]
    public async Task ReactivatesAReconcilerRetiredPendingRow()
    {
        var account = await AccountAsync();
        var row = await RetiredAsync(account);

        var result = await Sut().RepairAsync(dryRun: false);

        result.Should().Be(new RetiredPendingRepairResult(false, 1, 1, 0, 0, 0));
        var after = await Reload(row);
        after.IsActive.Should().BeTrue();
        after.DeletedAt.Should().BeNull();
        after.IsPending.Should().BeTrue("the provider has not reported it booked");
    }

    [Fact]
    public async Task SettlesTheRow_WhenTheProviderReportsItBooked()
    {
        var account = await AccountAsync();
        var row = await RetiredAsync(account);
        var posted = Day.AddDays(1);
        _fetcher.Setup(f => f.FetchAsync(It.Is<BankAccount>(a => a.Id == account.Id), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new TransactionCandidate(account.Id, UserId, -900m, Day, posted, "Rent", IsPending: false, "debit", null, null)]);

        var result = await Sut().RepairAsync(dryRun: false);

        result.Settled.Should().Be(1);
        var after = await Reload(row);
        after.IsActive.Should().BeTrue();
        after.IsPending.Should().BeFalse();
        after.PostedDate.Should().Be(posted);
    }

    [Fact]
    public async Task KeepsTheRowPending_WhenTheProviderStillReportsItPending()
    {
        var account = await AccountAsync();
        var row = await RetiredAsync(account);
        _fetcher.Setup(f => f.FetchAsync(It.IsAny<BankAccount>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new TransactionCandidate(account.Id, UserId, -900m, Day, null, "Rent", IsPending: true, "debit", null, null)]);

        var result = await Sut().RepairAsync(dryRun: false);

        result.Reactivated.Should().Be(1);
        result.Settled.Should().Be(0);
        (await Reload(row)).IsPending.Should().BeTrue();
    }

    [Fact]
    public async Task StillRestoresTheRow_WhenTheProviderCannotBeRead()
    {
        var account = await AccountAsync();
        var row = await RetiredAsync(account);
        _fetcher.Setup(f => f.FetchAsync(It.IsAny<BankAccount>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("429"));

        var result = await Sut().RepairAsync(dryRun: false);

        result.Reactivated.Should().Be(1);
        result.AccountsUnread.Should().Be(1);
        (await Reload(row)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SkipsARowWithAnActiveSameDayTwin()
    {
        var account = await AccountAsync();
        var row = await RetiredAsync(account);
        _db.Transactions.Add(new Transaction(account.Id, UserId, -900m, Day, "Rent (settled copy)", "other-hash", isPending: false)
        {
            TransactionType = "debit",
            PostedDate = Day,
        });
        await _db.SaveChangesAsync();

        var result = await Sut().RepairAsync(dryRun: false);

        result.EligibleBefore.Should().Be(0);
        (await Reload(row)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task KeepsARowWhoseSameDayTwinIsAnotherAmount()
    {
        var account = await AccountAsync();
        var row = await RetiredAsync(account);
        _db.Transactions.Add(new Transaction(account.Id, UserId, -12m, Day, "Coffee", "coffee", isPending: false) { TransactionType = "debit" });
        await _db.SaveChangesAsync();

        (await Sut().RepairAsync(dryRun: false)).Reactivated.Should().Be(1);
        (await Reload(row)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SkipsRowsRetiredBeforeTheReconcilerFirstRan()
    {
        var account = await AccountAsync();
        var row = await RetiredAsync(account, retiredAt: RetiredPendingRepairService.RetiredSince.AddMinutes(-1));

        (await Sut().RepairAsync(dryRun: false)).EligibleBefore.Should().Be(0);
        (await Reload(row)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SkipsRowsArchivedForAnotherReason()
    {
        var account = await AccountAsync();
        var row = await RetiredAsync(account, archivedReason: "account_deleted");

        (await Sut().RepairAsync(dryRun: false)).EligibleBefore.Should().Be(0);
        (await Reload(row)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SkipsRowsOnAnInactiveAccount()
    {
        var account = await AccountAsync(isActive: false);
        var row = await RetiredAsync(account);

        (await Sut().RepairAsync(dryRun: false)).EligibleBefore.Should().Be(0);
        (await Reload(row)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DryRunCountsButChangesNothing()
    {
        var account = await AccountAsync();
        var row = await RetiredAsync(account);

        var result = await Sut().RepairAsync(dryRun: true);

        result.Should().Be(new RetiredPendingRepairResult(true, 1, 0, 0, 1, 0));
        (await Reload(row)).IsActive.Should().BeFalse();
        _fetcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SecondRunChangesNothing()
    {
        var account = await AccountAsync();
        await RetiredAsync(account, "Rent");
        await RetiredAsync(account, "Gym", amount: -30m);

        var first = await Sut().RepairAsync(dryRun: false);
        var second = await Sut().RepairAsync(dryRun: false);

        first.Should().Be(new RetiredPendingRepairResult(false, 2, 2, 0, 0, 0));
        second.Should().Be(new RetiredPendingRepairResult(false, 0, 0, 0, 0, 0));
        (await _db.Transactions.IgnoreQueryFilters().CountAsync(t => t.IsActive)).Should().Be(2);
    }
}
