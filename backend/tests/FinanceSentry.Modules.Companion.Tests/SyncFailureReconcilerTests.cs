namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Jobs;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Regression guard for the late relay: 62 of 63 delivered financial SyncFailure events were already resolved when
/// they went out, while real multi-day outages arrived days late because staleness was only judged at capture.
/// </summary>
public sealed class SyncFailureReconcilerTests
{
    private static readonly Guid User = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly MaterialityPolicy Policy = new();

    private sealed class Alerts(params Guid[] resolved) : IMaterialAlertReader
    {
        public Task<IReadOnlyList<MaterialAlertRecord>> GetNewSinceAsync(
            DateTimeOffset watermark, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MaterialAlertRecord>>([]);

        public Task<IReadOnlySet<Guid>> GetResolvedIdsAsync(IReadOnlyCollection<Guid> alertIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<Guid>>(resolved.ToHashSet());
    }

    private sealed class Settings(NotificationMode mode) : INotificationSettingRepository
    {
        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(new CompanionNotificationSetting
            {
                UserId = userId,
                Mode = mode,
                TimeZoneId = "UTC",
                DigestHourLocal = DateTimeOffset.UtcNow.Hour,
            });

        public Task<CompanionNotificationSetting> GetOrDefaultUnscopedAsync(Guid userId, CancellationToken ct = default)
            => GetOrDefaultAsync(userId, ct);

        public Task UpsertAsync(CompanionNotificationSetting s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingDispatcher : IAgentWakeDispatcher
    {
        public int DigestWakes { get; private set; }

        public int LastHeldCount { get; private set; }

        public Task<WakeResult> WakeAsync(CompanionEvent evt, CancellationToken ct = default) => Task.FromResult(WakeResult.Sent);

        public Task<WakeResult> WakeDigestAsync(Guid userId, int heldCount, CancellationToken ct = default)
        {
            DigestWakes++;
            LastHeldCount = heldCount;
            return Task.FromResult(WakeResult.Sent);
        }
    }

    private sealed class Outbox
    {
        private readonly CompanionDbContext _db = new(
            new DbContextOptionsBuilder<CompanionDbContext>().UseInMemoryDatabase($"reconciler-{Guid.NewGuid():N}").Options,
            NoCurrentUser.Instance);

        public Outbox() => Repository = new CompanionEventRepository(_db);

        public CompanionEventRepository Repository { get; }

        // A no-principal read matches nothing under the Owner filter, so the assertions opt out like the jobs do.
        public async Task<EventDisposition> DispositionOfAsync(Guid id)
            => (await _db.Events.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking().SingleAsync(e => e.Id == id)).Disposition;
    }

    private static CompanionEvent SyncFailure(Guid alertId, TimeSpan age, EventDisposition disposition) => new()
    {
        UserId = User,
        Kind = CompanionEventKind.SyncFailure,
        Subject = "Monobank",
        Severity = "warning",
        Summary = "Sync failed",
        DedupKey = Policy.AlertDedupKey(alertId),
        ReferenceId = alertId,
        SourceModule = "alerts",
        Disposition = disposition,
        OccurredAt = DateTimeOffset.UtcNow - age,
    };

    private static SyncFailureReconciler NewReconciler(
        CompanionEventRepository events, NotificationMode mode, params Guid[] resolved)
        => new(new Alerts(resolved), new Settings(mode), events, Policy);

    [Fact]
    public async Task A_held_failure_whose_alert_resolved_is_expired_and_not_relayed_by_the_digest()
    {
        var outbox = new Outbox();
        var events = outbox.Repository;
        var alertId = Guid.NewGuid();
        var evt = SyncFailure(alertId, TimeSpan.FromHours(3), EventDisposition.HeldForDigest);
        await events.InsertIfNewAsync(evt);
        var dispatcher = new RecordingDispatcher();

        await new CompanionDigestJob(
            new Settings(NotificationMode.Digest), events, NewReconciler(events, NotificationMode.Digest, alertId),
            dispatcher, NullLogger<CompanionDigestJob>.Instance).ExecuteAsync();

        dispatcher.DigestWakes.Should().Be(0, "the only held event was already resolved");
        (await outbox.DispositionOfAsync(evt.Id)).Should().Be(EventDisposition.Expired);
    }

    [Fact]
    public async Task The_digest_counts_only_the_unresolved_failures()
    {
        var outbox = new Outbox();
        var events = outbox.Repository;
        var resolvedId = Guid.NewGuid();
        await events.InsertIfNewAsync(SyncFailure(resolvedId, TimeSpan.FromHours(3), EventDisposition.HeldForDigest));
        await events.InsertIfNewAsync(SyncFailure(Guid.NewGuid(), TimeSpan.FromHours(2), EventDisposition.HeldForDigest));
        var dispatcher = new RecordingDispatcher();

        await new CompanionDigestJob(
            new Settings(NotificationMode.Digest), events, NewReconciler(events, NotificationMode.Digest, resolvedId),
            dispatcher, NullLogger<CompanionDigestJob>.Instance).ExecuteAsync();

        dispatcher.LastHeldCount.Should().Be(1);
    }

    [Fact]
    public async Task A_pending_failure_whose_alert_resolved_is_expired_instead_of_dispatched()
    {
        var outbox = new Outbox();
        var events = outbox.Repository;
        var alertId = Guid.NewGuid();
        var evt = SyncFailure(alertId, TimeSpan.FromMinutes(30), EventDisposition.Pending);
        await events.InsertIfNewAsync(evt);
        var dispatcher = new RecordingDispatcher();

        var job = new CompanionDispatchJob(
            events, NewReconciler(events, NotificationMode.Realtime, alertId), new Settings(NotificationMode.Realtime),
            dispatcher, new StubUserAuthorizationChecker(User), Options.Create(new CompanionOptions()),
            NullLogger<CompanionDispatchJob>.Instance);
        await job.ExecuteAsync();

        (await outbox.DispositionOfAsync(evt.Id)).Should().Be(EventDisposition.Expired);
    }

    [Fact]
    public async Task A_held_failure_open_longer_than_a_day_escalates_to_pending_in_realtime_mode()
    {
        var outbox = new Outbox();
        var events = outbox.Repository;
        var evt = SyncFailure(Guid.NewGuid(), TimeSpan.FromHours(30), EventDisposition.HeldForDigest);
        await events.InsertIfNewAsync(evt);

        var remaining = await NewReconciler(events, NotificationMode.Realtime).ReconcileAsync([evt]);

        remaining.Should().ContainSingle().Which.Disposition.Should().Be(EventDisposition.Pending);
        (await outbox.DispositionOfAsync(evt.Id)).Should().Be(EventDisposition.Pending);
    }

    [Fact]
    public async Task A_held_failure_inside_a_day_stays_held()
    {
        var outbox = new Outbox();
        var events = outbox.Repository;
        var evt = SyncFailure(Guid.NewGuid(), TimeSpan.FromHours(23), EventDisposition.HeldForDigest);
        await events.InsertIfNewAsync(evt);

        await NewReconciler(events, NotificationMode.Realtime).ReconcileAsync([evt]);

        (await outbox.DispositionOfAsync(evt.Id)).Should().Be(EventDisposition.HeldForDigest);
    }

    [Fact]
    public async Task A_stale_failure_under_digest_mode_stays_held_for_the_digest()
    {
        var outbox = new Outbox();
        var events = outbox.Repository;
        var evt = SyncFailure(Guid.NewGuid(), TimeSpan.FromDays(3), EventDisposition.HeldForDigest);
        await events.InsertIfNewAsync(evt);

        await NewReconciler(events, NotificationMode.Digest).ReconcileAsync([evt]);

        (await outbox.DispositionOfAsync(evt.Id)).Should().Be(EventDisposition.HeldForDigest);
    }

    [Fact]
    public async Task Other_kinds_pass_through_untouched()
    {
        var outbox = new Outbox();
        var events = outbox.Repository;
        var evt = SyncFailure(Guid.NewGuid(), TimeSpan.FromDays(3), EventDisposition.Pending);
        evt.Kind = CompanionEventKind.NewsCluster;
        await events.InsertIfNewAsync(evt);

        var remaining = await NewReconciler(events, NotificationMode.Realtime, Policy.AlertIdFromDedupKey(evt.DedupKey)!.Value)
            .ReconcileAsync([evt]);

        remaining.Should().ContainSingle().Which.Disposition.Should().Be(EventDisposition.Pending);
    }
}
