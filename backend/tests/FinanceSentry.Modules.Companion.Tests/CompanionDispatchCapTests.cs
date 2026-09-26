namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Modules.Companion.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Report §5.6: the per-hour proactive cap already exists and must bound bursts — including
/// NewsCluster (N1), the loudest signal in the ledger-heartbeat design. Proven here rather than
/// asserted: a burst that exceeds <see cref="CompanionNotificationSetting.MaxProactivePerHour"/> is
/// deferred (<see cref="EventDisposition.SuppressedByRateLimit"/>), never dispatched, regardless of
/// which event kind fills the quota.
/// </summary>
public sealed class CompanionDispatchCapTests
{
    private static readonly Guid User = Guid.NewGuid();

    private sealed class FixedSettings(int maxProactivePerHour) : INotificationSettingRepository
    {
        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(new CompanionNotificationSetting
            {
                UserId = userId,
                Mode = NotificationMode.Realtime,
                MaxProactivePerHour = maxProactivePerHour,
                // No quiet hours configured — the cap, not the clock, is what's under test.
                QuietHoursStartLocal = null,
                QuietHoursEndLocal = null,
            });

        public Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CompanionNotificationSetting>> ListByModeAsync(
            NotificationMode mode, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanionNotificationSetting>>([]);
    }

    /// <summary>
    /// <see cref="DispatchedInLastHour"/> tracks live, like the real repository's rolling count — it
    /// increments as each event in the same batch dispatches, so the cap is enforced mid-batch and not
    /// just across ticks.
    /// </summary>
    private sealed class FakeEvents : ICompanionEventRepository
    {
        public List<CompanionEvent> Pending { get; } = [];

        public int DispatchedInLastHour { get; set; }

        public List<CompanionEvent> Updated { get; } = [];

        public Task<bool> InsertIfNewAsync(CompanionEvent evt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CompanionEvent>> ListByDispositionAsync(
            Guid userId, IReadOnlyCollection<EventDisposition> dispositions, int limit, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CompanionEvent>> ListRealtimePendingAsync(int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanionEvent>>([.. Pending]);

        public Task<IReadOnlyList<CompanionEvent>> ListHeldForDigestAsync(Guid userId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListHeldForDigestUserIdsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> CountDispatchedSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
            => Task.FromResult(DispatchedInLastHour);

        public Task<CompanionEvent?> GetAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CompanionEvent>> ListByDedupKeysAsync(
            Guid userId, IReadOnlyCollection<string> dedupKeys, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CompanionEvent>> ListByOccurredRangeAsync(
            Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(CompanionEvent evt, CancellationToken ct = default)
        {
            Updated.Add(evt);
            if (evt.Disposition == EventDisposition.Dispatched)
            {
                DispatchedInLastHour++;
            }

            return Task.CompletedTask;
        }

        public Task<int> MarkDeliveredAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class CountingDispatcher : IAgentWakeDispatcher
    {
        public int WakeCalls { get; private set; }

        public Task<WakeResult> WakeAsync(CompanionEvent evt, CancellationToken ct = default)
        {
            WakeCalls++;
            return Task.FromResult(WakeResult.Sent);
        }

        public Task<WakeResult> WakeDigestAsync(Guid userId, int heldCount, CancellationToken ct = default)
            => Task.FromResult(WakeResult.Sent);
    }

    private static CompanionEvent NewsClusterEvent(string ticker) => new()
    {
        UserId = User,
        Kind = CompanionEventKind.NewsCluster,
        Subject = ticker,
        Severity = "warning",
        Summary = $"News cluster: {ticker}",
        DedupKey = $"alert:{Guid.NewGuid()}",
        SourceModule = "alerts",
        Disposition = EventDisposition.Pending,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Burst_at_the_hourly_cap_is_deferred_rather_than_dispatched()
    {
        var events = new FakeEvents { DispatchedInLastHour = 6 };
        events.Pending.Add(NewsClusterEvent("AAPL"));
        var dispatcher = new CountingDispatcher();
        var job = new CompanionDispatchJob(
            events, new FixedSettings(maxProactivePerHour: 6), dispatcher,
            Options.Create(new CompanionOptions()), NullLogger<CompanionDispatchJob>.Instance);

        await job.ExecuteAsync();

        dispatcher.WakeCalls.Should().Be(0, "the hourly quota is already exhausted");
        events.Updated.Should().ContainSingle(e => e.Disposition == EventDisposition.SuppressedByRateLimit);
    }

    [Fact]
    public async Task Below_the_cap_the_event_still_dispatches()
    {
        var events = new FakeEvents { DispatchedInLastHour = 5 };
        events.Pending.Add(NewsClusterEvent("AAPL"));
        var dispatcher = new CountingDispatcher();
        var job = new CompanionDispatchJob(
            events, new FixedSettings(maxProactivePerHour: 6), dispatcher,
            Options.Create(new CompanionOptions()), NullLogger<CompanionDispatchJob>.Instance);

        await job.ExecuteAsync();

        dispatcher.WakeCalls.Should().Be(1);
        events.Updated.Should().ContainSingle(e => e.Disposition == EventDisposition.Dispatched);
    }

    [Fact]
    public async Task A_burst_of_several_pending_events_only_dispatches_up_to_the_remaining_quota()
    {
        var events = new FakeEvents { DispatchedInLastHour = 5 };
        events.Pending.Add(NewsClusterEvent("AAPL"));
        events.Pending.Add(NewsClusterEvent("MU"));
        events.Pending.Add(NewsClusterEvent("TSM"));
        var dispatcher = new CountingDispatcher();
        var job = new CompanionDispatchJob(
            events, new FixedSettings(maxProactivePerHour: 6), dispatcher,
            Options.Create(new CompanionOptions()), NullLogger<CompanionDispatchJob>.Instance);

        await job.ExecuteAsync();

        // Only one slot remains under the cap (5 of 6 already used) — the first event in the batch
        // takes it and pushes the live count to 6, so the rest of the burst defers within this same tick.
        dispatcher.WakeCalls.Should().Be(1);
        events.Updated.Count(e => e.Disposition == EventDisposition.SuppressedByRateLimit).Should().Be(2);
    }
}
