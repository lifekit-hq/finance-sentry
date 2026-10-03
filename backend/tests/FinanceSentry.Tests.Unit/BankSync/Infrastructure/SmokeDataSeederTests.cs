namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Seeding;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

/// <summary>
/// The smoke account's fake bank data (<see cref="SmokeDataSeeder"/>): written once for that user only, at
/// startup with no principal, and excluded from every cross-user active-account read.
/// </summary>
public class SmokeDataSeederTests
{
    private static readonly Guid SmokeUserId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _database = $"smoke-seed-{Guid.NewGuid():N}";

    [Fact]
    public void Seed_WritesOneSeededAccountWithTransactionsForThatUser()
    {
        using (var db = NewContext())
            SmokeDataSeeder.Seed(db, SmokeUserId, Now).Should().BeTrue();

        using var check = NewContext();
        var account = check.BankAccounts.IgnoreQueryFilters().Should().ContainSingle().Subject;
        account.UserId.Should().Be(SmokeUserId);
        account.Provider.Should().Be(BankAccount.SeededProvider);
        account.SyncStatus.Should().Be("active");
        account.CurrentBalance.Should().BePositive();

        var transactions = check.Transactions.IgnoreQueryFilters().ToList();
        transactions.Should().NotBeEmpty();
        transactions.Should().OnlyContain(t => t.UserId == SmokeUserId && t.AccountId == account.Id);
        transactions.Should().Contain(t => t.TransactionType == "credit").And.Contain(t => t.TransactionType == "debit");
        transactions.Should().OnlyContain(t => t.TransactionDate.Month == Now.Month && t.TransactionDate <= Now);
    }

    [Fact]
    public void Seed_OnTheFirstDayOfTheMonth_DatesNothingInTheFuture()
    {
        var firstOfMonth = new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc);

        using (var db = NewContext())
            SmokeDataSeeder.Seed(db, SmokeUserId, firstOfMonth);

        using var check = NewContext();
        check.Transactions.IgnoreQueryFilters().ToList()
            .Should().OnlyContain(t => t.TransactionDate == new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Seed_RunAgain_IsANoOp()
    {
        using (var db = NewContext())
            SmokeDataSeeder.Seed(db, SmokeUserId, Now);

        using (var db = NewContext())
            SmokeDataSeeder.Seed(db, SmokeUserId, Now.AddMonths(1)).Should().BeFalse();

        using var check = NewContext();
        check.BankAccounts.IgnoreQueryFilters().Should().ContainSingle();
    }

    [Fact]
    public async Task CrossUserActiveRead_ExcludesTheSeededAccount()
    {
        var realUser = Guid.NewGuid();
        using (var db = NewContext())
        {
            db.BankAccounts.Add(new BankAccount(realUser, "ext-1", "Bank", "checking", "1234", "Owner", "EUR", Guid.NewGuid(), "monobank"));
            db.SaveChanges();
            SmokeDataSeeder.Seed(db, SmokeUserId, Now).Should().BeTrue();
        }

        using var read = NewContext();
        var active = await new BankAccountRepository(read).GetAllActiveUnscopedAsync();

        active.Select(a => a.UserId).Should().Equal(realUser);
    }

    [Fact]
    public async Task ActiveUserIds_OmitTheSeededUser()
    {
        var realUser = Guid.NewGuid();
        using (var db = NewContext())
        {
            db.BankAccounts.Add(new BankAccount(realUser, "ext-1", "Bank", "checking", "1234", "Owner", "EUR", Guid.NewGuid(), "monobank"));
            db.SaveChanges();
            SmokeDataSeeder.Seed(db, SmokeUserId, Now);
        }

        using var read = NewContext();
        var ids = await new BankingTotalsReader(new BankAccountRepository(read), Mock.Of<ISyncJobRepository>())
            .GetActiveUserIdsAsync();

        ids.Should().Equal(realUser);
    }

    private BankSyncDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BankSyncDbContext>().UseInMemoryDatabase(_database).Options, NoCurrentUser.Instance);
}
