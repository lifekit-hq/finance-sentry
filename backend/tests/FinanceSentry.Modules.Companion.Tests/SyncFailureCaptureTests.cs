namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Capture threads the referenced bank account's last successful sync into the materiality decision:
/// a SyncFailure is held for the digest unless that account has been without a successful sync for
/// more than a day. Provider-level failures (no account reference) have no staleness and are held.
/// </summary>
public sealed class SyncFailureCaptureTests
{
    private static readonly Guid User = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Account = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private sealed class RealtimeSettings : INotificationSettingRepository
    {
        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(new CompanionNotificationSetting { UserId = userId, Mode = NotificationMode.Realtime });

        public Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CompanionNotificationSetting>> ListByModeAsync(
            NotificationMode mode, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanionNotificationSetting>>([]);
    }

    private sealed class OneAlert(MaterialAlertRecord alert) : IMaterialAlertReader
    {
        public Task<IReadOnlyList<MaterialAlertRecord>> GetNewSinceAsync(
            DateTimeOffset watermark, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MaterialAlertRecord>>([alert]);
    }

    private sealed class NoAnalystActions : IAnalystActionFeedReader
    {
        public Task<IReadOnlyList<AnalystActionFeedRecord>> GetNewSinceAsync(
            DateTimeOffset watermark, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AnalystActionFeedRecord>>([]);
    }

    private sealed class NoHoldings : IBrokerageHoldingsReader
    {
        public Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BrokerageHoldingSummary>>([]);
    }

    private sealed class NoBankingTotals : IBankingTotalsReader
    {
        public Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<decimal> GetTotalUsdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(0m);

        public Task<DateTime?> GetLatestSuccessfulSyncAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<DateTime?>(null);
    }

    private sealed class Accounts(DateTime? lastSuccessfulSync) : IBankingAccountsReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<BankingAccountSummary>> GetAccountSummariesAsync(Guid userId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<BankingAccountSummary>>(
            [
                new BankingAccountSummary(
                    Account, "Revolut", "checking", "1234", "truelayer", "EUR", 100m, 110m, "failed",
                    DateTime.UtcNow, lastSuccessfulSync),
            ]);
        }

        public Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([User]);

        public Task<IReadOnlyList<AccountBalanceSnapshot>> GetActiveAccountSnapshotsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountBalanceSnapshot>>([]);
    }

    private static MaterialAlertRecord SyncFailure(Guid? referenceId) => new(
        Guid.NewGuid(), User, "SyncFailure", "error", "Sync failed for Revolut", referenceId, "Revolut", DateTimeOffset.UtcNow);

    private static async Task<CompanionEvent> CaptureAsync(MaterialAlertRecord alert, IBankingAccountsReader accounts)
    {
        await using var db = new CompanionDbContext(
            new DbContextOptionsBuilder<CompanionDbContext>()
                .UseInMemoryDatabase($"sync-failure-{Guid.NewGuid():N}").Options);
        var capture = new CompanionEventCapture(
            new OneAlert(alert),
            new NoAnalystActions(),
            new NoHoldings(),
            new NoBankingTotals(),
            accounts,
            new RealtimeSettings(),
            new CompanionEventRepository(db),
            new CompanionCaptureStateRepository(db),
            new MaterialityPolicy(),
            Options.Create(new CompanionOptions()),
            NullLogger<CompanionEventCapture>.Instance);

        (await capture.CaptureAsync()).Should().Be(1);
        return await db.Events.SingleAsync();
    }

    [Fact]
    public async Task Recently_synced_account_failure_is_held_for_digest()
    {
        var evt = await CaptureAsync(SyncFailure(Account), new Accounts(DateTime.UtcNow.AddHours(-2)));

        evt.Kind.Should().Be(CompanionEventKind.SyncFailure);
        evt.Disposition.Should().Be(EventDisposition.HeldForDigest);
    }

    [Fact]
    public async Task Account_stale_past_a_day_is_pending_for_realtime_wake()
    {
        var evt = await CaptureAsync(SyncFailure(Account), new Accounts(DateTime.UtcNow.AddHours(-30)));

        evt.Disposition.Should().Be(EventDisposition.Pending);
    }

    [Fact]
    public async Task Never_synced_account_is_held_for_digest()
    {
        var evt = await CaptureAsync(SyncFailure(Account), new Accounts(null));

        evt.Disposition.Should().Be(EventDisposition.HeldForDigest);
    }

    [Fact]
    public async Task Provider_level_failure_without_account_reference_is_held_without_an_account_lookup()
    {
        var accounts = new Accounts(DateTime.UtcNow.AddDays(-10));

        var evt = await CaptureAsync(SyncFailure(null), accounts);

        evt.Disposition.Should().Be(EventDisposition.HeldForDigest);
        accounts.Calls.Should().Be(0);
    }
}
