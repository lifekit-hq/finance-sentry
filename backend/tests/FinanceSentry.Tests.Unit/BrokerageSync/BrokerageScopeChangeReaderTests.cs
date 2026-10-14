namespace FinanceSentry.Tests.Unit.BrokerageSync;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class BrokerageScopeChangeReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Since = DateTime.UtcNow.AddDays(-30);
    private static readonly DateTime LongAgo = Since.AddDays(-60);

    private static BrokerageSyncDbContext NewDb() => new(
        new DbContextOptionsBuilder<BrokerageSyncDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        NoCurrentUser.Instance);

    private static IBKRCredential Ibkr(DateTime createdAt)
    {
        var credential = new IBKRCredential(UserId, "key", "token", "dh", [], [], [], [], [], [], [], [], [], 1);
        typeof(IBKRCredential).GetProperty(nameof(IBKRCredential.CreatedAt))!.SetValue(credential, createdAt);
        return credential;
    }

    private static IBKRFlexCredential Flex(DateTime createdAt)
    {
        var credential = new IBKRFlexCredential(UserId, "query", [], [], [], 1);
        typeof(IBKRFlexCredential).GetProperty(nameof(IBKRFlexCredential.CreatedAt))!.SetValue(credential, createdAt);
        return credential;
    }

    [Fact]
    public async Task ReportsAnOauthCredentialRelinkedInsideTheWindow()
    {
        await using var db = NewDb();
        var credential = Ibkr(LongAgo);
        credential.Deactivate();
        credential.Reactivate("key", "token", "dh", [], [], [], [], [], [], [], [], [], 1);
        db.IBKRCredentials.Add(credential);
        await db.SaveChangesAsync();

        var changes = await new BrokerageScopeChangeReader(db).GetChangesSinceAsync(UserId, Since);

        changes.Should().ContainSingle(c => c.Source == "ibkr" && c.Kind == AccountScopeChangeKinds.Linked);
    }

    [Fact]
    public async Task ReportsAFlexCredentialRelinkedInsideTheWindow()
    {
        await using var db = NewDb();
        var credential = Flex(LongAgo);
        credential.Deactivate();
        credential.Replace("query", [], [], [], 1);
        db.IBKRFlexCredentials.Add(credential);
        await db.SaveChangesAsync();

        var changes = await new BrokerageScopeChangeReader(db).GetChangesSinceAsync(UserId, Since);

        changes.Should().ContainSingle(c => c.Source == "ibkr-flex" && c.Kind == AccountScopeChangeKinds.Linked);
    }

    [Fact]
    public async Task DoesNotReportAFlexTokenReplacedWhileStillActive()
    {
        await using var db = NewDb();
        var credential = Flex(LongAgo);
        credential.Replace("query", [], [], [], 1);
        db.IBKRFlexCredentials.Add(credential);
        await db.SaveChangesAsync();

        var changes = await new BrokerageScopeChangeReader(db).GetChangesSinceAsync(UserId, Since);

        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task DoesNotReportACredentialLinkedInsideTheWindowThenDeactivated()
    {
        await using var db = NewDb();
        var ibkr = Ibkr(Since.AddDays(5));
        ibkr.Deactivate();
        var flex = Flex(Since.AddDays(5));
        flex.Deactivate();
        db.IBKRCredentials.Add(ibkr);
        db.IBKRFlexCredentials.Add(flex);
        await db.SaveChangesAsync();

        var changes = await new BrokerageScopeChangeReader(db).GetChangesSinceAsync(UserId, Since);

        changes.Should().BeEmpty();
    }
}
