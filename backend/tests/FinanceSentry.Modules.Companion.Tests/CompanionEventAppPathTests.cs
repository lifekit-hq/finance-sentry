namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Auth;
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

/// <summary>#466 N-A: a captured event keeps the app path of the thing it is about, so the push can open it.</summary>
public sealed class CompanionEventAppPathTests
{
    private static readonly Guid User = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private sealed class RealtimeSettings : INotificationSettingRepository
    {
        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(new CompanionNotificationSetting { UserId = userId, Mode = NotificationMode.Realtime });

        public Task<CompanionNotificationSetting> GetOrDefaultUnscopedAsync(Guid userId, CancellationToken ct = default)
            => GetOrDefaultAsync(userId, ct);

        public Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class Alerts(params MaterialAlertRecord[] rows) : IMaterialAlertReader
    {
        public Task<IReadOnlyList<MaterialAlertRecord>> GetNewSinceAsync(
            DateTimeOffset watermark, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MaterialAlertRecord>>(rows);

        public Task<IReadOnlySet<Guid>> GetOpenIdsAsync(IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<Guid>>(alertIds.ToHashSet());
    }

    private sealed class Actions(params AnalystActionFeedRecord[] rows) : IAnalystActionFeedReader
    {
        public Task<IReadOnlyList<AnalystActionFeedRecord>> GetNewSinceAsync(
            DateTimeOffset watermark, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AnalystActionFeedRecord>>(rows);
    }

    private sealed class Holds(string symbol) : IBrokerageHoldingsReader
    {
        public Task<IReadOnlyList<BrokerageHoldingSummary>> GetHoldingsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BrokerageHoldingSummary>>(
                [new BrokerageHoldingSummary(symbol, "Equity", 1m, 100m, DateTime.UtcNow, "ibkr")]);
    }

    private sealed class Banking : IBankingTotalsReader
    {
        public Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([User]);

        public Task<decimal> GetTotalUsdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(0m);

        public Task<DateTime?> GetLatestSuccessfulSyncAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<DateTime?>(null);
    }

    private sealed class NoAccounts : IBankingAccountsReader
    {
        public Task<IReadOnlyList<BankingAccountSummary>> GetAccountSummariesAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BankingAccountSummary>>([]);

        public Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([User]);

        public Task<IReadOnlyList<AccountBalanceSnapshot>> GetActiveAccountSnapshotsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountBalanceSnapshot>>([]);
    }

    private static async Task<List<CompanionEvent>> CaptureAsync(IMaterialAlertReader alerts, IAnalystActionFeedReader actions, string held)
    {
        await using var db = new CompanionDbContext(
            new DbContextOptionsBuilder<CompanionDbContext>()
                .UseInMemoryDatabase($"app-path-{Guid.NewGuid():N}").Options,
            NoCurrentUser.Instance);
        var capture = new CompanionEventCapture(
            alerts, actions, new Holds(held), new Banking(), new NoAccounts(), new RealtimeSettings(),
            new CompanionEventRepository(db), new CompanionCaptureStateRepository(db), new MaterialityPolicy(),
            Options.Create(new CompanionOptions()), NullLogger<CompanionEventCapture>.Instance);

        await capture.CaptureAsync();
        return await db.Events.IgnoreQueryFilters([OwnerQueryFilter.Name]).ToListAsync();
    }

    [Fact]
    public async Task An_alert_event_keeps_the_path_the_alert_resolved()
    {
        var alert = new MaterialAlertRecord(
            Guid.NewGuid(), User, "ThesisBroken", "critical", "Thesis broken: NVDA", Guid.NewGuid(), "NVDA",
            DateTimeOffset.UtcNow, "/assets/NVDA");

        var events = await CaptureAsync(new Alerts(alert), new Actions(), "MSFT");

        events.Should().ContainSingle().Which.AppPath.Should().Be("/assets/NVDA");
    }

    [Fact]
    public async Task An_alert_with_no_target_leaves_the_event_without_one()
    {
        var alert = new MaterialAlertRecord(
            Guid.NewGuid(), User, "JobFailure", "critical", "Job failed", null, "SyncJob", DateTimeOffset.UtcNow);

        var events = await CaptureAsync(new Alerts(alert), new Actions(), "MSFT");

        events.Should().ContainSingle().Which.AppPath.Should().BeNull();
    }

    [Fact]
    public async Task An_analyst_action_opens_the_dossier_of_its_ticker()
    {
        var action = new AnalystActionFeedRecord(Guid.NewGuid(), "nvda", "Acme Securities", "Upgrade", 150m, DateTimeOffset.UtcNow);

        var events = await CaptureAsync(new Alerts(), new Actions(action), "NVDA");

        events.Should().ContainSingle().Which.AppPath.Should().Be("/assets/NVDA");
    }
}
