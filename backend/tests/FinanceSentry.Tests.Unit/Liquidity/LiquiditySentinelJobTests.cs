namespace FinanceSentry.Tests.Unit.Liquidity;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Liquidity.Application.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class LiquiditySentinelJobTests
{
    private readonly Mock<IBankingAccountsReader> _accountsReader = new();
    private readonly Mock<IActiveSubscriptionsReader> _subscriptionsReader = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly LiquiditySentinelJob _job;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();

    public LiquiditySentinelJobTests()
    {
        _job = new LiquiditySentinelJob(
            _accountsReader.Object,
            _subscriptionsReader.Object,
            _alerts.Object,
            NullLogger<LiquiditySentinelJob>.Instance);
    }

    [Fact]
    public async Task Execute_NoActiveUsers_SkipsGracefully()
    {
        _accountsReader.Setup(r => r.GetAllActiveUserIdsAsync(default))
            .ReturnsAsync([]);

        await _job.ExecuteAsync();

        _accountsReader.Verify(r => r.GetActiveAccountSnapshotsAsync(It.IsAny<Guid>(), default), Times.Never);
        _alerts.Verify(a => a.GenerateCashShortfallAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<decimal>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_AccountWithNullBalance_Skipped()
    {
        _accountsReader.Setup(r => r.GetAllActiveUserIdsAsync(default))
            .ReturnsAsync([_userId]);
        _accountsReader.Setup(r => r.GetActiveAccountSnapshotsAsync(_userId, default))
            .ReturnsAsync([new AccountBalanceSnapshot(_accountId, "Chase", "1234", "EUR", null)]);
        _subscriptionsReader.Setup(r => r.GetActiveSubscriptionsAsync(_userId, default))
            .ReturnsAsync([]);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateCashShortfallAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<decimal>(), It.IsAny<string>(), default), Times.Never);
        _alerts.Verify(a => a.ResolveCashShortfallAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task Execute_ShortfallDetected_FiresAlert()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dueDate = today.AddDays(5);

        _accountsReader.Setup(r => r.GetAllActiveUserIdsAsync(default))
            .ReturnsAsync([_userId]);
        _accountsReader.Setup(r => r.GetActiveAccountSnapshotsAsync(_userId, default))
            .ReturnsAsync([new AccountBalanceSnapshot(_accountId, "Chase", "1234", "EUR", 100m)]);
        _subscriptionsReader.Setup(r => r.GetActiveSubscriptionsAsync(_userId, default))
            .ReturnsAsync([new ActiveSubscriptionSummary("Netflix", "monthly", 200m, "EUR", dueDate)]);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateCashShortfallAlertAsync(
            _userId,
            _accountId,
            "Chase ···1234",
            dueDate,
            100m,
            "EUR",
            default), Times.Once);
    }

    [Fact]
    public async Task Execute_NoShortfall_ResolvesExistingAlert()
    {
        _accountsReader.Setup(r => r.GetAllActiveUserIdsAsync(default))
            .ReturnsAsync([_userId]);
        _accountsReader.Setup(r => r.GetActiveAccountSnapshotsAsync(_userId, default))
            .ReturnsAsync([new AccountBalanceSnapshot(_accountId, "Chase", "1234", "EUR", 10000m)]);
        _subscriptionsReader.Setup(r => r.GetActiveSubscriptionsAsync(_userId, default))
            .ReturnsAsync([new ActiveSubscriptionSummary("Netflix", "monthly", 15m, "EUR", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5))]);

        await _job.ExecuteAsync();

        _alerts.Verify(a => a.GenerateCashShortfallAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<decimal>(), It.IsAny<string>(), default), Times.Never);
        _alerts.Verify(a => a.ResolveCashShortfallAlertAsync(_userId, _accountId, default), Times.Once);
    }

    [Fact]
    public async Task Execute_MultipleAccounts_ProcessesEach()
    {
        var accountId2 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        _accountsReader.Setup(r => r.GetAllActiveUserIdsAsync(default))
            .ReturnsAsync([_userId]);
        _accountsReader.Setup(r => r.GetActiveAccountSnapshotsAsync(_userId, default))
            .ReturnsAsync([
                new AccountBalanceSnapshot(_accountId, "Chase", "1234", "EUR", 50m),
                new AccountBalanceSnapshot(accountId2, "Revolut", "5678", "EUR", 5000m),
            ]);
        _subscriptionsReader.Setup(r => r.GetActiveSubscriptionsAsync(_userId, default))
            .ReturnsAsync([new ActiveSubscriptionSummary("Netflix", "monthly", 100m, "EUR", today.AddDays(3))]);

        await _job.ExecuteAsync();

        // First account shortfalls; second account doesn't
        _alerts.Verify(a => a.GenerateCashShortfallAlertAsync(
            _userId, _accountId, It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<decimal>(), "EUR", default),
            Times.Once);
        _alerts.Verify(a => a.ResolveCashShortfallAlertAsync(_userId, accountId2, default), Times.Once);
    }

    [Fact]
    public async Task Execute_UserThrows_ContinuesToNextUser()
    {
        var userId2 = Guid.NewGuid();
        var accountId2 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        _accountsReader.Setup(r => r.GetAllActiveUserIdsAsync(default))
            .ReturnsAsync([_userId, userId2]);

        _accountsReader.Setup(r => r.GetActiveAccountSnapshotsAsync(_userId, default))
            .ThrowsAsync(new InvalidOperationException("simulated error"));

        _accountsReader.Setup(r => r.GetActiveAccountSnapshotsAsync(userId2, default))
            .ReturnsAsync([new AccountBalanceSnapshot(accountId2, "AIB", "9999", "EUR", 5000m)]);
        _subscriptionsReader.Setup(r => r.GetActiveSubscriptionsAsync(userId2, default))
            .ReturnsAsync([]);

        // Should not throw — errors per user are caught
        await _job.ExecuteAsync();

        _alerts.Verify(a => a.ResolveCashShortfallAlertAsync(userId2, accountId2, default), Times.Once);
    }
}
