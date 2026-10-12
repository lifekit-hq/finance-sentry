namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="StaleSyncReaperJob"/> (TrueLayer #3). Verifies the startup sweep releases
/// syncs orphaned by a crash/restart — the deadlock that froze Revolut for 5 days.
/// </summary>
public class StaleSyncReaperJobTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly Mock<ISyncJobRepository> _jobs = new();
    private readonly Mock<IBankAccountRepository> _accounts = new();

    public StaleSyncReaperJobTests()
    {
        _jobs.Setup(r => r.GetByStatusUnscopedAsync("running", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SyncJob>());
        _accounts.Setup(r => r.GetBySyncStatusUnscopedAsync("syncing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BankAccount>());
    }

    [Fact]
    public async Task Startup_ReapsRunningJobAndSyncingAccount()
    {
        var job = new SyncJob(Guid.NewGuid(), UserId);
        var account = MakeSyncingAccount();
        _jobs.Setup(r => r.GetByStatusUnscopedAsync("running", It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);
        _accounts.Setup(r => r.GetBySyncStatusUnscopedAsync("syncing", It.IsAny<CancellationToken>()))
            .ReturnsAsync([account]);

        await MakeReaper().ExecuteAsync();

        job.Status.Should().Be("failed");
        job.ErrorCode.Should().Be("STALE_JOB_REAPED");
        account.SyncStatus.Should().Be("active");
        account.LastSyncError.Should().BeNull("a reaped sync is not a provider failure");
        _jobs.Verify(r => r.UpdateAsync(job, It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(r => r.UpdateAsync(account, It.IsAny<CancellationToken>()), Times.Once);
    }

    private StaleSyncReaperJob MakeReaper()
        => new(_jobs.Object, _accounts.Object, Mock.Of<ILogger<StaleSyncReaperJob>>());

    private static BankAccount MakeSyncingAccount()
    {
        var account = new BankAccount(UserId, "item_abc123", "REVOLUT-IE", "checking",
            "1234", "John Doe", "EUR", UserId, "truelayer");
        account.BeginSync();
        return account;
    }
}
