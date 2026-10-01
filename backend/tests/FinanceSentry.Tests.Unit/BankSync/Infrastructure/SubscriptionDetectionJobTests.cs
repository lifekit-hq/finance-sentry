namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// <see cref="SubscriptionDetectionJob"/> runs as a background job with no person in scope, so it must
/// still read every user's transactions and accounts despite the Owner query filter.
/// </summary>
public class SubscriptionDetectionJobTests
{
    private const int MonthlyCharges = 4;
    private const int DaysBetweenCharges = 30;

    private readonly Mock<ISubscriptionDetectionResultService> _results = new();

    private static BankSyncDbContext NewDb(string name) => new(
        new DbContextOptionsBuilder<BankSyncDbContext>().UseInMemoryDatabase(name).Options,
        NoCurrentUser.Instance);

    private static void SeedMonthlyCharges(BankSyncDbContext db, Guid userId, string merchant)
    {
        var account = new BankAccount(userId, Guid.NewGuid().ToString(), "Test Bank",
            "current", "1234", "Owner", "EUR", Guid.NewGuid(), "monobank");
        db.BankAccounts.Add(account);

        for (var i = 0; i < MonthlyCharges; i++)
        {
            db.Transactions.Add(new Transaction(account.Id, userId, 9.99m,
                DateTime.UtcNow.AddDays(-DaysBetweenCharges * i - 1), merchant, Guid.NewGuid().ToString())
            {
                MerchantName = merchant,
                IsActive = true,
            });
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithNoPersonInScope_DetectsSubscriptionsForEveryUser()
    {
        await using var db = NewDb($"subs-{Guid.NewGuid():N}");
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        SeedMonthlyCharges(db, userA, "Netflix");
        SeedMonthlyCharges(db, userB, "Spotify");
        await db.SaveChangesAsync();

        await new SubscriptionDetectionJob(db, _results.Object, Mock.Of<ILogger<SubscriptionDetectionJob>>())
            .ExecuteAsync();

        _results.Verify(r => r.UpsertDetectedSubscriptionsAsync(
            userA.ToString(),
            It.Is<IReadOnlyList<DetectedSubscriptionData>>(d => d.Count == 1 && d[0].MerchantNameNormalized == "netflix"),
            It.IsAny<CancellationToken>()), Times.Once);
        _results.Verify(r => r.UpsertDetectedSubscriptionsAsync(
            userB.ToString(),
            It.Is<IReadOnlyList<DetectedSubscriptionData>>(d => d.Count == 1 && d[0].MerchantNameNormalized == "spotify"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
