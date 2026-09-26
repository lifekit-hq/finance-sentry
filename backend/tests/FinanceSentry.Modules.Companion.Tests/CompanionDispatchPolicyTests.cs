namespace FinanceSentry.Modules.Companion.Tests;

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
/// Issue #686: proves the realtime dispatch relay's policy gates (quiet hours, per-hour cap) actually
/// execute against a real, persisted <see cref="CompanionNotificationSetting"/> row — the settings
/// table being empty in production meant this path never ran.
/// </summary>
public sealed class CompanionDispatchPolicyTests
{
    private static readonly Guid User = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private sealed class FixedSettings(CompanionNotificationSetting setting) : INotificationSettingRepository
    {
        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(setting);

        public Task UpsertAsync(CompanionNotificationSetting s, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CompanionNotificationSetting>> ListByModeAsync(
            NotificationMode mode, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanionNotificationSetting>>([]);
    }

    private sealed class RecordingDispatcher : IAgentWakeDispatcher
    {
        public int WakeCalls { get; private set; }

        public Task<WakeResult> WakeAsync(CompanionEvent evt, CancellationToken ct = default)
        {
            WakeCalls++;
            return Task.FromResult(WakeResult.Sent);
        }

        public Task<WakeResult> WakeDigestAsync(Guid userId, int heldCount, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static CompanionEvent PendingEvent(DateTimeOffset occurredAt) => new()
    {
        UserId = User,
        Kind = CompanionEventKind.UnusualSpend,
        Subject = "test-subject",
        Severity = "info",
        Summary = "test",
        DedupKey = $"dedup-{Guid.NewGuid():N}",
        SourceModule = "alerts",
        Disposition = EventDisposition.Pending,
        OccurredAt = occurredAt,
    };

    private static CompanionEventRepository NewEventRepository()
    {
        var db = new CompanionDbContext(
            new DbContextOptionsBuilder<CompanionDbContext>()
                .UseInMemoryDatabase($"dispatch-policy-{Guid.NewGuid():N}").Options);
        return new CompanionEventRepository(db);
    }

    [Fact]
    public async Task Realtime_event_is_deferred_during_quiet_hours()
    {
        var events = NewEventRepository();
        var evt = PendingEvent(DateTimeOffset.UtcNow.AddMinutes(-5));
        await events.InsertIfNewAsync(evt);

        var nowUtcHour = DateTimeOffset.UtcNow.UtcDateTime.Hour;
        var setting = new CompanionNotificationSetting
        {
            UserId = User,
            Mode = NotificationMode.Realtime,
            TimeZoneId = "UTC",
            QuietHoursStartLocal = nowUtcHour,
            QuietHoursEndLocal = (nowUtcHour + 1) % 24,
            MaxProactivePerHour = 6,
        };

        var dispatcher = new RecordingDispatcher();
        var job = new CompanionDispatchJob(
            events, new FixedSettings(setting), dispatcher,
            Options.Create(new CompanionOptions()), NullLogger<CompanionDispatchJob>.Instance);

        await job.ExecuteAsync();

        dispatcher.WakeCalls.Should().Be(0);
        (await events.GetAsync(evt.Id))!.Disposition.Should().Be(EventDisposition.DeferredQuietHours);
    }

    [Fact]
    public async Task Realtime_event_is_suppressed_once_the_per_hour_cap_is_reached()
    {
        var events = NewEventRepository();

        for (var i = 0; i < 3; i++)
        {
            var dispatched = PendingEvent(DateTimeOffset.UtcNow.AddMinutes(-30));
            dispatched.Disposition = EventDisposition.Dispatched;
            dispatched.DispatchedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
            await events.InsertIfNewAsync(dispatched);
        }

        var evt = PendingEvent(DateTimeOffset.UtcNow.AddMinutes(-1));
        await events.InsertIfNewAsync(evt);

        var setting = new CompanionNotificationSetting
        {
            UserId = User,
            Mode = NotificationMode.Realtime,
            TimeZoneId = "UTC",
            QuietHoursStartLocal = null,
            QuietHoursEndLocal = null,
            MaxProactivePerHour = 3,
        };

        var dispatcher = new RecordingDispatcher();
        var job = new CompanionDispatchJob(
            events, new FixedSettings(setting), dispatcher,
            Options.Create(new CompanionOptions()), NullLogger<CompanionDispatchJob>.Instance);

        await job.ExecuteAsync();

        dispatcher.WakeCalls.Should().Be(0);
        (await events.GetAsync(evt.Id))!.Disposition.Should().Be(EventDisposition.SuppressedByRateLimit);
    }

    [Fact]
    public async Task Realtime_event_dispatches_once_below_the_cap_and_outside_quiet_hours()
    {
        var events = NewEventRepository();
        var evt = PendingEvent(DateTimeOffset.UtcNow.AddMinutes(-1));
        await events.InsertIfNewAsync(evt);

        var setting = new CompanionNotificationSetting
        {
            UserId = User,
            Mode = NotificationMode.Realtime,
            TimeZoneId = "UTC",
            QuietHoursStartLocal = null,
            QuietHoursEndLocal = null,
            MaxProactivePerHour = 6,
        };

        var dispatcher = new RecordingDispatcher();
        var job = new CompanionDispatchJob(
            events, new FixedSettings(setting), dispatcher,
            Options.Create(new CompanionOptions()), NullLogger<CompanionDispatchJob>.Instance);

        await job.ExecuteAsync();

        dispatcher.WakeCalls.Should().Be(1);
        (await events.GetAsync(evt.Id))!.Disposition.Should().Be(EventDisposition.Dispatched);
    }
}
