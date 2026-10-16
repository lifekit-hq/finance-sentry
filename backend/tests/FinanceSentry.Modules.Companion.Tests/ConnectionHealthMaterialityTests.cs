namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Companion materiality by class for a provider the connection-health policy drives (slice S5): a confirmed credential
/// loss goes out proactively in every mode, Quiet included (decided 2026-10-06); a Failing outage keeps the sync-failure
/// rule; the Dormant notice is one digest line. An alert the policy did not raise (a shadow provider) is judged as before.
/// </summary>
public sealed class ConnectionHealthMaterialityTests
{
    private static readonly Guid User = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly MaterialityPolicy Policy = new();

    [Theory]
    [InlineData(NotificationMode.Quiet)]
    [InlineData(NotificationMode.Digest)]
    [InlineData(NotificationMode.Scan)]
    [InlineData(NotificationMode.Realtime)]
    public void A_confirmed_credential_loss_is_relayed_at_once_in_every_mode(NotificationMode mode) =>
        Policy.DispositionFor(mode, CompanionEventKind.SyncFailure, sourceStaleness: null, healthState: "ActionRequired")
            .Should().Be(EventDisposition.Pending);

    [Theory]
    [InlineData(NotificationMode.Quiet, EventDisposition.SuppressedByMode)]
    [InlineData(NotificationMode.Digest, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Realtime, EventDisposition.HeldForDigest)]
    public void A_sync_failure_the_policy_did_not_escalate_keeps_the_sync_failure_rule(
        NotificationMode mode, EventDisposition expected) =>
        Policy.DispositionFor(mode, CompanionEventKind.SyncFailure, TimeSpan.FromHours(2), healthState: null)
            .Should().Be(expected);

    [Theory]
    [InlineData(NotificationMode.Quiet, EventDisposition.SuppressedByMode)]
    [InlineData(NotificationMode.Digest, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Scan, EventDisposition.HeldForDigest)]
    [InlineData(NotificationMode.Realtime, EventDisposition.HeldForDigest)]
    public void The_Dormant_notice_is_one_digest_line_never_a_wake(NotificationMode mode, EventDisposition expected) =>
        Policy.DispositionFor(mode, CompanionEventKind.ConnectionDormant, null, "Dormant").Should().Be(expected);

    [Fact]
    public void The_Dormant_notice_does_not_escalate_however_long_it_stays_open() =>
        Policy.DispositionFor(NotificationMode.Realtime, CompanionEventKind.ConnectionDormant, TimeSpan.FromDays(30))
            .Should().Be(EventDisposition.HeldForDigest);

    // ── Capture threads the alert's health state into the decision ──

    private sealed class ModeSettings(NotificationMode mode) : INotificationSettingRepository
    {
        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(new CompanionNotificationSetting { UserId = userId, Mode = mode });

        public Task<CompanionNotificationSetting> GetOrDefaultUnscopedAsync(Guid userId, CancellationToken ct = default)
            => GetOrDefaultAsync(userId, ct);

        public Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Alerts(IReadOnlyList<MaterialAlertRecord> created, params Guid[] notOpen) : IMaterialAlertReader
    {
        public Task<IReadOnlyList<MaterialAlertRecord>> GetNewSinceAsync(
            DateTimeOffset watermark, int limit, CancellationToken ct = default) => Task.FromResult(created);

        public Task<IReadOnlySet<Guid>> GetOpenIdsAsync(IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<Guid>>(alertIds.Except(notOpen).ToHashSet());
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

    private sealed class NoBanking : IBankingTotalsReader, IBankingAccountsReader
    {
        public Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<decimal> GetTotalUsdAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(0m);

        public Task<DateTime?> GetLatestSuccessfulSyncAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<DateTime?>(null);

        public Task<IReadOnlyList<BankingAccountSummary>> GetAccountSummariesAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BankingAccountSummary>>([]);

        public Task<IReadOnlyList<Guid>> GetAllActiveUserIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<AccountBalanceSnapshot>> GetActiveAccountSnapshotsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountBalanceSnapshot>>([]);
    }

    private static MaterialAlertRecord Alert(string type, string? healthState) => new(
        Guid.NewGuid(), User, type, "Error", "Sync failed for ibkr", null, null, DateTimeOffset.UtcNow,
        "/accounts/list?reconnect=ibkr", healthState);

    private static async Task<CompanionEvent> CaptureAsync(MaterialAlertRecord alert, NotificationMode mode)
    {
        await using var store = new InMemoryCompanionStore($"health-{Guid.NewGuid():N}", NoCurrentUser.Instance);
        var banking = new NoBanking();
        var capture = new CompanionEventCapture(
            new Alerts([alert]), new NoAnalystActions(), new NoHoldings(), banking, banking, new ModeSettings(mode),
            store, store, Policy, Options.Create(new CompanionOptions()), NullLogger<CompanionEventCapture>.Instance);

        (await capture.CaptureAsync()).Should().Be(1);
        return (await store.AllEventsAsync()).Single();
    }

    [Fact]
    public async Task The_reconnect_ask_is_captured_as_pending_even_in_Quiet_mode()
    {
        var evt = await CaptureAsync(Alert("SyncFailure", "ActionRequired"), NotificationMode.Quiet);

        evt.Kind.Should().Be(CompanionEventKind.SyncFailure);
        evt.Disposition.Should().Be(EventDisposition.Pending);
        evt.AppPath.Should().Be("/accounts/list?reconnect=ibkr");
    }

    [Fact]
    public async Task A_shadow_providers_credential_alert_stays_silent_in_Quiet_mode_as_before()
    {
        var evt = await CaptureAsync(Alert("SyncFailure", null), NotificationMode.Quiet);

        evt.Disposition.Should().Be(EventDisposition.SuppressedByMode);
    }

    [Fact]
    public async Task The_Dormant_notice_is_captured_for_the_digest()
    {
        var evt = await CaptureAsync(Alert("ConnectionDormant", "Dormant"), NotificationMode.Realtime);

        evt.Kind.Should().Be(CompanionEventKind.ConnectionDormant);
        evt.Disposition.Should().Be(EventDisposition.HeldForDigest);
    }

    // ── Delivery drops a Dormant line once the connection is back ──

    private static CompanionEvent Dormant(Guid alertId) => new()
    {
        UserId = User,
        Kind = CompanionEventKind.ConnectionDormant,
        Subject = "Monobank ···1234",
        Severity = "Info",
        Summary = "Monobank ···1234 may be closed",
        DedupKey = Policy.AlertDedupKey(alertId),
        ReferenceId = alertId,
        SourceModule = "alerts",
        Disposition = EventDisposition.HeldForDigest,
        OccurredAt = DateTimeOffset.UtcNow.AddDays(-2),
    };

    [Fact]
    public async Task A_Dormant_line_whose_notice_resolved_is_expired_and_an_open_one_stays_held()
    {
        await using var store = new InMemoryCompanionStore($"health-{Guid.NewGuid():N}", NoCurrentUser.Instance);
        var healed = Guid.NewGuid();
        var stillDormant = Guid.NewGuid();
        var healedEvent = Dormant(healed);
        var openEvent = Dormant(stillDormant);
        await store.InsertIfNewAsync(healedEvent);
        await store.InsertIfNewAsync(openEvent);

        var kept = await new SyncFailureReconciler(
                new Alerts([], healed), new ModeSettings(NotificationMode.Realtime), store, Policy)
            .ReconcileAsync([healedEvent, openEvent]);

        kept.Should().ContainSingle().Which.Id.Should().Be(openEvent.Id);
        var stored = await store.AllEventsAsync();
        stored.Single(e => e.Id == healedEvent.Id).Disposition.Should().Be(EventDisposition.Expired);
        stored.Single(e => e.Id == openEvent.Id).Disposition.Should().Be(EventDisposition.HeldForDigest);
    }
}
