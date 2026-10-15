namespace FinanceSentry.Tests.Unit.BankSync.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class BankAccountScopeChangeReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Since = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BankAccount Account(
        Guid userId, string externalId, DateTime createdAt, bool isActive = true, DateTime? updatedAt = null)
    {
        var account = new BankAccount(userId, externalId, "Bank", "savings", "1234", "Owner", "EUR", userId, "truelayer")
        {
            IsActive = isActive,
        };
        typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(account, createdAt);
        if (updatedAt is not null)
            typeof(Entity).GetProperty(nameof(Entity.UpdatedAt))!.SetValue(account, updatedAt);
        return account;
    }

    private static BankSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<BankSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    [Fact]
    public async Task ReportsOnlyTheAccountLinkedInsideTheWindow()
    {
        await using var db = NewDb();
        db.BankAccounts.AddRange(
            Account(UserId, "before", Since.AddDays(-30)),
            Account(UserId, "inside", Since.AddDays(10)),
            Account(Guid.NewGuid(), "someone-else", Since.AddDays(10)));
        await db.SaveChangesAsync();

        var changes = await new BankAccountScopeChangeReader(db).GetChangesSinceAsync(UserId, Since);

        changes.Should().ContainSingle()
            .Which.Should().Match<AccountScopeChange>(c =>
                c.Kind == AccountScopeChangeKinds.Linked && c.AtUtc == Since.AddDays(10));
    }

    [Fact]
    public async Task ReportsNothingWhenEveryAccountPredatesTheWindow()
    {
        await using var db = NewDb();
        db.BankAccounts.Add(Account(UserId, "before", Since.AddDays(-30)));
        await db.SaveChangesAsync();

        var changes = await new BankAccountScopeChangeReader(db).GetChangesSinceAsync(UserId, Since);

        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportsAnAccountDeactivatedInsideTheWindowAsRemoved()
    {
        await using var db = NewDb();
        db.BankAccounts.Add(Account(UserId, "gone", Since.AddDays(-30), isActive: false, updatedAt: Since.AddDays(5)));
        await db.SaveChangesAsync();

        var changes = await new BankAccountScopeChangeReader(db).GetChangesSinceAsync(UserId, Since);

        changes.Should().ContainSingle()
            .Which.Should().Match<AccountScopeChange>(c =>
                c.Kind == AccountScopeChangeKinds.Removed && c.AtUtc == Since.AddDays(5));
    }

    [Fact]
    public async Task IgnoresAnAccountDeactivatedBeforeTheWindow()
    {
        await using var db = NewDb();
        db.BankAccounts.Add(Account(UserId, "long-gone", Since.AddDays(-60), isActive: false, updatedAt: Since.AddDays(-20)));
        await db.SaveChangesAsync();

        var changes = await new BankAccountScopeChangeReader(db).GetChangesSinceAsync(UserId, Since);

        changes.Should().BeEmpty();
    }
}
