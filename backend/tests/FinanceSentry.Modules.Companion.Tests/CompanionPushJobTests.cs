namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Spec 859: the Web Push sender. Audience (own alerts for everyone, OperationalFailure to ops.admin only), independence
/// from <see cref="CompanionEvent.Disposition"/> and the notification mode, idempotency per (event, subscription), the
/// status-code matrix and pruning. In-memory fakes stand in for the repository and the push service.
/// </summary>
public sealed class CompanionPushJobTests
{
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private sealed class FakeRepository : IPushDeliveryRepository
    {
        public List<PushSubscription> Subscriptions { get; } = [];

        public List<CompanionEvent> Events { get; } = [];

        public List<PushDelivery> Deliveries { get; } = [];

        public List<DateTimeOffset> Since { get; } = [];

        public int Saves { get; private set; }

        public int Pruned { get; private set; }

        public Task<IReadOnlyList<PushSubscription>> ListActiveSubscriptionsUnscopedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PushSubscription>>([.. Subscriptions.Where(s => s.DisabledAt is null)]);

        public Task<IReadOnlyList<CompanionEvent>> ListUndeliveredEventsUnscopedAsync(
            Guid userId, Guid subscriptionId, DateTimeOffset since, bool includeOperational, int limit, CancellationToken ct = default)
        {
            Since.Add(since);
            IReadOnlyList<CompanionEvent> events =
            [
                .. Events
                    .Where(e => e.UserId == userId
                                && e.CapturedAt >= since
                                && (includeOperational || e.Kind != CompanionEventKind.OperationalFailure)
                                && !Deliveries.Any(d => d.EventId == e.Id && d.SubscriptionId == subscriptionId))
                    .OrderBy(e => e.CapturedAt)
                    .Take(limit),
            ];
            return Task.FromResult(events);
        }

        public Task AddDeliveriesAsync(IReadOnlyCollection<PushDelivery> deliveries, CancellationToken ct = default)
        {
            foreach (var d in deliveries.Where(d => !Deliveries.Any(x => x.EventId == d.EventId && x.SubscriptionId == d.SubscriptionId)))
                Deliveries.Add(d);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PushDelivery>> ListDueUnscopedAsync(DateTimeOffset now, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PushDelivery>>([
                .. Deliveries.Where(d => d.Status == PushDeliveryStatus.Pending && (d.NextAttemptAt is null || d.NextAttemptAt <= now)).Take(limit)]);

        public Task<IReadOnlyDictionary<Guid, CompanionEvent>> GetEventsUnscopedAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, CompanionEvent>>(Events.Where(e => ids.Contains(e.Id)).ToDictionary(e => e.Id));

        public Task<IReadOnlyDictionary<Guid, PushSubscription>> GetSubscriptionsUnscopedAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, PushSubscription>>(Subscriptions.Where(s => ids.Contains(s.Id)).ToDictionary(s => s.Id));

        public Task RemoveSubscriptionUnscopedAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            Subscriptions.RemoveAll(s => s.Id == subscriptionId);
            Deliveries.RemoveAll(d => d.SubscriptionId == subscriptionId);
            return Task.CompletedTask;
        }

        public Task<int> PruneDisabledSubscriptionsUnscopedAsync(DateTimeOffset disabledBefore, CancellationToken ct = default)
        {
            Pruned = Subscriptions.RemoveAll(s => s.DisabledAt < disabledBefore);
            return Task.FromResult(Pruned);
        }

        public Task SaveAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender(Func<PushSubscription, PushSendResult>? respond = null) : IPushSender
    {
        public List<(Guid SubscriptionId, string Payload)> Sent { get; } = [];

        public Task<PushSendResult> SendAsync(PushSubscription subscription, string payload, CancellationToken ct = default)
        {
            Sent.Add((subscription.Id, payload));
            return Task.FromResult(respond?.Invoke(subscription) ?? new PushSendResult(PushSendOutcome.Sent, 201));
        }
    }

    private static PushSubscription Sub(Guid userId, DateTimeOffset? createdAt = null) => new()
    {
        UserId = userId,
        Endpoint = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}",
        P256dh = "p",
        Auth = "a",
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-1),
    };

    private static CompanionEvent Evt(
        Guid userId,
        CompanionEventKind kind = CompanionEventKind.LowBalance,
        EventDisposition disposition = EventDisposition.Pending,
        DateTimeOffset? capturedAt = null) => new()
        {
            UserId = userId,
            Kind = kind,
            Subject = "Monobank UAH",
            Severity = "warning",
            Summary = "Balance fell to $12.50",
            DedupKey = $"k:{Guid.NewGuid():N}",
            SourceModule = "alerts",
            Disposition = disposition,
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow.AddMinutes(-1),
        };

    private static CompanionPushJob Job(FakeRepository repo, IPushSender sender, bool configured = true)
        => new(
            repo,
            sender,
            new StubUserAuthorizationChecker(Admin),
            Options.Create(configured ? new WebPushOptions { PublicKey = "pub", PrivateKey = "priv" } : new WebPushOptions()),
            NullLogger<CompanionPushJob>.Instance);

    [Fact]
    public async Task An_event_is_pushed_to_each_of_its_owners_devices_with_the_headline_only()
    {
        var repo = new FakeRepository();
        var phone = Sub(Member);
        var laptop = Sub(Member);
        repo.Subscriptions.AddRange([phone, laptop, Sub(Admin)]);
        repo.Events.Add(Evt(Member));
        var sender = new FakeSender();

        await Job(repo, sender).ExecuteAsync();

        sender.Sent.Select(s => s.SubscriptionId).Should().BeEquivalentTo([phone.Id, laptop.Id]);
        sender.Sent.Should().OnlyContain(s => s.Payload.Contains("Low balance") && s.Payload.Contains("Monobank UAH"));
        sender.Sent.Should().OnlyContain(s => !s.Payload.Contains("12.50"));
        repo.Deliveries.Should().OnlyContain(d => d.Status == PushDeliveryStatus.Sent && d.SentAt != null && d.Attempts == 1);
    }

    [Fact]
    public async Task Overlapping_and_repeated_runs_never_push_the_same_event_twice()
    {
        var repo = new FakeRepository();
        repo.Subscriptions.Add(Sub(Member));
        repo.Events.Add(Evt(Member));
        var sender = new FakeSender();
        var job = Job(repo, sender);

        await job.ExecuteAsync();
        await job.ExecuteAsync();
        await job.ExecuteAsync();

        sender.Sent.Should().HaveCount(1);
        repo.Deliveries.Should().HaveCount(1);
    }

    [Fact]
    public async Task OperationalFailure_goes_only_to_ops_admin_holders()
    {
        var repo = new FakeRepository();
        var memberDevice = Sub(Member);
        var adminDevice = Sub(Admin);
        repo.Subscriptions.AddRange([memberDevice, adminDevice]);
        repo.Events.AddRange([
            Evt(Member, CompanionEventKind.OperationalFailure),
            Evt(Admin, CompanionEventKind.OperationalFailure),
            Evt(Member, CompanionEventKind.SyncFailure),
        ]);
        var sender = new FakeSender();

        await Job(repo, sender).ExecuteAsync();

        var byDevice = sender.Sent.GroupBy(s => s.SubscriptionId).ToDictionary(g => g.Key, g => g.Select(x => x.Payload).ToList());
        byDevice[adminDevice.Id].Should().ContainSingle().Which.Should().Contain("Operational failure");
        byDevice[memberDevice.Id].Should().ContainSingle().Which.Should().Contain("Sync failure")
            .And.NotContain("Operational failure");
    }

    [Fact]
    public async Task A_role_lost_after_queueing_expires_the_pending_OperationalFailure_push()
    {
        var repo = new FakeRepository();
        var device = Sub(Member);
        repo.Subscriptions.Add(device);
        var evt = Evt(Member, CompanionEventKind.OperationalFailure);
        repo.Events.Add(evt);
        repo.Deliveries.Add(new PushDelivery { EventId = evt.Id, SubscriptionId = device.Id, UserId = Member });
        var sender = new FakeSender();

        await Job(repo, sender).ExecuteAsync();

        sender.Sent.Should().BeEmpty();
        repo.Deliveries.Should().ContainSingle().Which.Status.Should().Be(PushDeliveryStatus.Expired);
    }

    [Theory]
    [InlineData(EventDisposition.Pending)]
    [InlineData(EventDisposition.Dispatched)]
    [InlineData(EventDisposition.Delivered)]
    [InlineData(EventDisposition.DeferredQuietHours)]
    [InlineData(EventDisposition.SuppressedByRateLimit)]
    [InlineData(EventDisposition.SuppressedNonOwner)]
    [InlineData(EventDisposition.Failed)]
    public async Task Push_ignores_the_disposition_and_never_changes_it(EventDisposition disposition)
    {
        var repo = new FakeRepository();
        repo.Subscriptions.Add(Sub(Member));
        var evt = Evt(Member, disposition: disposition);
        repo.Events.Add(evt);
        var sender = new FakeSender();

        await Job(repo, sender).ExecuteAsync();

        sender.Sent.Should().HaveCount(1);
        evt.Disposition.Should().Be(disposition);
        evt.DispatchedAt.Should().BeNull();
        evt.DeliveredAt.Should().BeNull();
        evt.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task The_ledger_is_saved_after_every_send_so_a_later_failure_cannot_cause_a_resend()
    {
        var repo = new FakeRepository();
        repo.Subscriptions.Add(Sub(Member));
        repo.Subscriptions.Add(Sub(Member));
        repo.Events.Add(Evt(Member));
        var saves = new List<int>();
        var sender = new FakeSender(_ =>
        {
            saves.Add(repo.Saves);
            return new PushSendResult(PushSendOutcome.Sent, 201);
        });

        await Job(repo, sender).ExecuteAsync();

        saves.Should().HaveCount(2);
        saves[1].Should().BeGreaterThan(saves[0]);
    }

    [Fact]
    public async Task A_keyless_deployment_does_nothing()
    {
        var repo = new FakeRepository();
        repo.Subscriptions.Add(Sub(Member));
        repo.Events.Add(Evt(Member));
        var sender = new FakeSender();

        await Job(repo, sender, configured: false).ExecuteAsync();

        sender.Sent.Should().BeEmpty();
        repo.Deliveries.Should().BeEmpty();
        repo.Saves.Should().Be(0);
    }

    [Fact]
    public async Task A_new_device_does_not_receive_the_backlog_from_before_it_subscribed()
    {
        var repo = new FakeRepository();
        var now = DateTimeOffset.UtcNow;
        repo.Subscriptions.Add(Sub(Member, createdAt: now.AddMinutes(-10)));
        repo.Events.AddRange([
            Evt(Member, capturedAt: now.AddHours(-3)),
            Evt(Member, capturedAt: now.AddMinutes(-5)),
        ]);
        var sender = new FakeSender();

        await Job(repo, sender).ExecuteAsync();

        sender.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task Events_older_than_the_window_are_not_pushed()
    {
        var repo = new FakeRepository();
        repo.Subscriptions.Add(Sub(Member, createdAt: DateTimeOffset.UtcNow.AddDays(-10)));
        repo.Events.Add(Evt(Member, capturedAt: DateTimeOffset.UtcNow - PushDeliveryPolicy.EventWindow - TimeSpan.FromMinutes(5)));
        var sender = new FakeSender();

        await Job(repo, sender).ExecuteAsync();

        sender.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task A_subscription_the_push_service_reports_gone_is_removed_with_its_deliveries(int status)
    {
        var repo = new FakeRepository();
        var dead = Sub(Member);
        var alive = Sub(Member);
        repo.Subscriptions.AddRange([dead, alive]);
        repo.Events.AddRange([Evt(Member), Evt(Member, CompanionEventKind.SyncFailure)]);
        var sender = new FakeSender(s => s.Id == dead.Id
            ? new PushSendResult(PushSendOutcome.Gone, status)
            : new PushSendResult(PushSendOutcome.Sent, 201));

        await Job(repo, sender).ExecuteAsync();

        repo.Subscriptions.Should().ContainSingle().Which.Id.Should().Be(alive.Id);
        repo.Deliveries.Should().OnlyContain(d => d.SubscriptionId == alive.Id);
        sender.Sent.Count(s => s.SubscriptionId == dead.Id).Should().Be(1, "the second event is not sent to a device already known to be gone");
    }

    [Fact]
    public async Task A_transient_error_backs_off_then_retries_and_gives_up_after_the_cap()
    {
        var repo = new FakeRepository();
        var device = Sub(Member);
        repo.Subscriptions.Add(device);
        repo.Events.Add(Evt(Member));
        var sender = new FakeSender(_ => new PushSendResult(PushSendOutcome.Transient, 503));
        var job = Job(repo, sender);

        await job.ExecuteAsync();

        var delivery = repo.Deliveries.Single();
        delivery.Status.Should().Be(PushDeliveryStatus.Pending);
        delivery.Attempts.Should().Be(1);
        delivery.LastStatusCode.Should().Be(503);
        delivery.NextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow);
        device.FailureCount.Should().Be(0, "a push-service outage does not count against the device");

        await job.ExecuteAsync();
        sender.Sent.Should().HaveCount(1, "the retry waits for its backoff");

        for (var attempt = 2; attempt <= PushDeliveryPolicy.MaxAttempts; attempt++)
        {
            delivery.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await job.ExecuteAsync();
        }

        delivery.Status.Should().Be(PushDeliveryStatus.Failed);
        delivery.Attempts.Should().Be(PushDeliveryPolicy.MaxAttempts);
        delivery.NextAttemptAt.Should().BeNull();
        repo.Subscriptions.Should().Contain(device);
    }

    [Fact]
    public async Task A_recovered_retry_is_marked_Sent_and_clears_the_failure_count()
    {
        var repo = new FakeRepository();
        var device = Sub(Member);
        device.FailureCount = 2;
        repo.Subscriptions.Add(device);
        repo.Events.Add(Evt(Member));
        var calls = 0;
        var sender = new FakeSender(_ => ++calls == 1
            ? new PushSendResult(PushSendOutcome.Transient, 429)
            : new PushSendResult(PushSendOutcome.Sent, 201));
        var job = Job(repo, sender);

        await job.ExecuteAsync();
        repo.Deliveries.Single().NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await job.ExecuteAsync();

        var delivery = repo.Deliveries.Single();
        delivery.Status.Should().Be(PushDeliveryStatus.Sent);
        delivery.Attempts.Should().Be(2);
        device.FailureCount.Should().Be(0);
        device.LastSuccessAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(413)]
    public async Task A_rejected_message_fails_at_once_without_retry_and_keeps_the_device(int status)
    {
        var repo = new FakeRepository();
        var device = Sub(Member);
        repo.Subscriptions.Add(device);
        repo.Events.Add(Evt(Member));
        var sender = new FakeSender(_ => new PushSendResult(PushSendOutcome.Rejected, status));
        var job = Job(repo, sender);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        sender.Sent.Should().HaveCount(1);
        repo.Deliveries.Single().Status.Should().Be(PushDeliveryStatus.Failed);
        repo.Deliveries.Single().LastStatusCode.Should().Be(status);
        device.DisabledAt.Should().BeNull();
        repo.Subscriptions.Should().Contain(device);
    }

    [Fact]
    public async Task Repeated_VAPID_rejections_soft_disable_the_subscription()
    {
        var repo = new FakeRepository();
        var device = Sub(Member);
        repo.Subscriptions.Add(device);
        repo.Events.AddRange(Enumerable.Range(0, PushDeliveryPolicy.DisableAfterRejections + 1).Select(_ => Evt(Member)));
        var sender = new FakeSender(_ => new PushSendResult(PushSendOutcome.Unauthorized, 403));
        var job = Job(repo, sender);

        for (var run = 0; run < PushDeliveryPolicy.DisableAfterRejections + 1; run++)
            await job.ExecuteAsync();

        device.DisabledAt.Should().NotBeNull();
        device.FailureCount.Should().Be(PushDeliveryPolicy.DisableAfterRejections);
        repo.Subscriptions.Should().Contain(device, "soft-disabled, not deleted, until the prune window passes");
    }

    [Fact]
    public async Task A_soft_disabled_subscription_is_pruned_after_the_prune_window()
    {
        var repo = new FakeRepository();
        var stale = Sub(Member);
        stale.DisabledAt = DateTimeOffset.UtcNow - PushDeliveryPolicy.PruneDisabledAfter - TimeSpan.FromDays(1);
        var recent = Sub(Member);
        recent.DisabledAt = DateTimeOffset.UtcNow.AddDays(-1);
        repo.Subscriptions.AddRange([stale, recent]);

        await Job(repo, new FakeSender()).ExecuteAsync();

        repo.Subscriptions.Should().ContainSingle().Which.Should().Be(recent);
    }

    [Fact]
    public async Task OperationalFailure_audience_is_checked_against_the_ops_admin_policy()
    {
        var repo = new FakeRepository();
        repo.Subscriptions.Add(Sub(Admin));
        repo.Events.Add(Evt(Admin, CompanionEventKind.OperationalFailure));
        var checker = new StubUserAuthorizationChecker(Admin);
        var job = new CompanionPushJob(
            repo, new FakeSender(), checker, Options.Create(new WebPushOptions { PublicKey = "p", PrivateKey = "k" }),
            NullLogger<CompanionPushJob>.Instance);

        await job.ExecuteAsync();

        checker.CheckedPolicies.Should().OnlyContain(p => p == AuthPolicies.RequireOwner);
    }
}
