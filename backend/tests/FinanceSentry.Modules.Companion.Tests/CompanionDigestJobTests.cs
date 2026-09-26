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
using Xunit;

/// <summary>
/// Issue #686: <see cref="CompanionDigestJob"/> must wake the agent for held events at the user's
/// configured local digest hour, regardless of persisted proactivity mode — <see cref="MaterialityPolicy"/>
/// holds some kinds (e.g. SyncFailure) for the digest even under Scan/Realtime, so gating on mode alone
/// stranded them.
/// </summary>
public sealed class CompanionDigestJobTests
{
    private static readonly Guid User = Guid.Parse("88888888-8888-8888-8888-888888888888");

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
        public int DigestWakes { get; private set; }

        public int LastHeldCount { get; private set; }

        public Task<WakeResult> WakeAsync(CompanionEvent evt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<WakeResult> WakeDigestAsync(Guid userId, int heldCount, CancellationToken ct = default)
        {
            DigestWakes++;
            LastHeldCount = heldCount;
            return Task.FromResult(WakeResult.Sent);
        }
    }

    private static CompanionEventRepository NewEventRepository()
    {
        var db = new CompanionDbContext(
            new DbContextOptionsBuilder<CompanionDbContext>()
                .UseInMemoryDatabase($"digest-job-{Guid.NewGuid():N}").Options);
        return new CompanionEventRepository(db);
    }

    private static CompanionEvent HeldEvent() => new()
    {
        UserId = User,
        Kind = CompanionEventKind.SyncFailure,
        Subject = "test-subject",
        Severity = "warning",
        Summary = "test",
        DedupKey = $"dedup-{Guid.NewGuid():N}",
        SourceModule = "alerts",
        Disposition = EventDisposition.HeldForDigest,
        OccurredAt = DateTimeOffset.UtcNow.AddHours(-1),
    };

    [Fact]
    public async Task Held_events_wake_the_digest_at_the_configured_local_hour_even_in_realtime_mode()
    {
        var events = NewEventRepository();
        await events.InsertIfNewAsync(HeldEvent());

        var nowUtcHour = DateTimeOffset.UtcNow.UtcDateTime.Hour;
        var setting = new CompanionNotificationSetting
        {
            UserId = User,
            Mode = NotificationMode.Realtime,
            TimeZoneId = "UTC",
            DigestHourLocal = nowUtcHour,
        };

        var dispatcher = new RecordingDispatcher();
        var job = new CompanionDigestJob(
            new FixedSettings(setting), events, dispatcher, NullLogger<CompanionDigestJob>.Instance);

        await job.ExecuteAsync();

        dispatcher.DigestWakes.Should().Be(1);
        dispatcher.LastHeldCount.Should().Be(1);
    }

    [Fact]
    public async Task No_wake_outside_the_configured_local_hour()
    {
        var events = NewEventRepository();
        await events.InsertIfNewAsync(HeldEvent());

        var nowUtcHour = DateTimeOffset.UtcNow.UtcDateTime.Hour;
        var setting = new CompanionNotificationSetting
        {
            UserId = User,
            Mode = NotificationMode.Scan,
            TimeZoneId = "UTC",
            DigestHourLocal = (nowUtcHour + 12) % 24,
        };

        var dispatcher = new RecordingDispatcher();
        var job = new CompanionDigestJob(
            new FixedSettings(setting), events, dispatcher, NullLogger<CompanionDigestJob>.Instance);

        await job.ExecuteAsync();

        dispatcher.DigestWakes.Should().Be(0);
    }

    [Fact]
    public async Task No_wake_when_nothing_is_held()
    {
        var events = NewEventRepository();

        var nowUtcHour = DateTimeOffset.UtcNow.UtcDateTime.Hour;
        var setting = new CompanionNotificationSetting
        {
            UserId = User,
            Mode = NotificationMode.Digest,
            TimeZoneId = "UTC",
            DigestHourLocal = nowUtcHour,
        };

        var dispatcher = new RecordingDispatcher();
        var job = new CompanionDigestJob(
            new FixedSettings(setting), events, dispatcher, NullLogger<CompanionDigestJob>.Instance);

        await job.ExecuteAsync();

        dispatcher.DigestWakes.Should().Be(0);
    }
}
