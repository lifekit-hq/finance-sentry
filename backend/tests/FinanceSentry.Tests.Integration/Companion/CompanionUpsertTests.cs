namespace FinanceSentry.Tests.Integration.Companion;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The Companion writes are each one <c>INSERT ... ON CONFLICT</c> on an existing unique index (event DedupKey,
/// setting UserId, push Endpoint, capture-state Source): repeating a key keeps one row, and writers racing on the
/// same key do not fail on the index. The in-memory provider enforces neither the index nor raw SQL, so this
/// needs real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CompanionUpsertTests : IAsyncLifetime
{
    private readonly Guid _user = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // No person in scope, like the jobs that call these repositories; assertions opt out of the Owner filter.
    private CompanionDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CompanionDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(null));

    private NotificationSettingRepository Settings(CompanionDbContext ctx) =>
        new(ctx, Options.Create(new CompanionOptions()));

    private CompanionEvent NewEvent(string dedupKey, string summary = "s") => new()
    {
        UserId = _user,
        Kind = CompanionEventKind.NewsCluster,
        Subject = "MU",
        Severity = "warning",
        Summary = summary,
        DedupKey = dedupKey,
        ReferenceId = Guid.NewGuid(),
        AppPath = "/assets/MU",
        SourceModule = "alerts",
        Disposition = EventDisposition.HeldForDigest,
        OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
    };

    private async Task<List<CompanionEvent>> EventsAsync()
    {
        await using var ctx = CreateContext();
        return await ctx.Events.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(e => e.UserId == _user).ToListAsync();
    }

    [DockerRequiredFact]
    public async Task A_repeated_event_with_one_dedup_key_is_kept_once_and_reports_not_new()
    {
        var first = NewEvent("alert:1", "first");
        await using (var ctx = CreateContext())
            (await new CompanionEventRepository(ctx).InsertIfNewAsync(first)).Should().BeTrue();

        await using (var ctx = CreateContext())
            (await new CompanionEventRepository(ctx).InsertIfNewAsync(NewEvent("alert:1", "second"))).Should().BeFalse();

        var row = (await EventsAsync()).Should().ContainSingle().Subject;
        row.Id.Should().Be(first.Id);
        row.Summary.Should().Be("first", "the first capture wins");
        row.Kind.Should().Be(CompanionEventKind.NewsCluster);
        row.Disposition.Should().Be(EventDisposition.HeldForDigest);
        row.ReferenceId.Should().Be(first.ReferenceId);
        row.AppPath.Should().Be("/assets/MU");
    }

    [DockerRequiredFact]
    public async Task Concurrent_captures_of_one_event_insert_it_once_without_failing()
    {
        var captures = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = CreateContext();
            return await new CompanionEventRepository(ctx).InsertIfNewAsync(NewEvent("alert:race"));
        });

        var results = await Task.WhenAll(captures);

        results.Count(inserted => inserted).Should().Be(1);
        (await EventsAsync()).Should().ContainSingle();
    }

    [DockerRequiredFact]
    public async Task A_repeated_setting_upsert_keeps_one_row_and_updates_it()
    {
        await using (var ctx = CreateContext())
            await Settings(ctx).UpsertAsync(new CompanionNotificationSetting
            {
                UserId = _user, Mode = NotificationMode.Scan, TimeZoneId = "UTC", MaxProactivePerHour = 3, DigestHourLocal = 8,
            });

        await using (var ctx = CreateContext())
            await Settings(ctx).UpsertAsync(new CompanionNotificationSetting
            {
                UserId = _user, Mode = NotificationMode.Realtime, QuietHoursStartLocal = 22, QuietHoursEndLocal = 7,
                TimeZoneId = "Europe/Dublin", MaxProactivePerHour = 5, DigestHourLocal = 9, PushEnabled = true,
            });

        await using var read = CreateContext();
        var row = (await read.NotificationSettings.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(s => s.UserId == _user).ToListAsync()).Should().ContainSingle().Subject;
        row.Mode.Should().Be(NotificationMode.Realtime);
        row.QuietHoursStartLocal.Should().Be(22);
        row.QuietHoursEndLocal.Should().Be(7);
        row.TimeZoneId.Should().Be("Europe/Dublin");
        row.MaxProactivePerHour.Should().Be(5);
        row.DigestHourLocal.Should().Be(9);
        row.PushEnabled.Should().BeTrue();
    }

    [DockerRequiredFact]
    public async Task Concurrent_setting_upserts_for_one_user_neither_fail_nor_duplicate()
    {
        var saves = Enumerable.Range(1, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await Settings(ctx).UpsertAsync(new CompanionNotificationSetting { UserId = _user, MaxProactivePerHour = i });
        });

        await Task.WhenAll(saves);

        await using var read = CreateContext();
        (await read.NotificationSettings.IgnoreQueryFilters([OwnerQueryFilter.Name]).CountAsync(s => s.UserId == _user))
            .Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task Registering_one_endpoint_twice_keeps_one_row_with_its_id_and_clears_failure_state()
    {
        const string endpoint = "https://fcm.googleapis.com/fcm/send/device-1";
        var first = new PushSubscription { UserId = _user, Endpoint = endpoint, P256dh = "k1", Auth = "a1", DeviceLabel = "Phone" };
        await using (var ctx = CreateContext())
            await new PushSubscriptionRepository(ctx).UpsertByEndpointAsync(first);

        await using (var ctx = CreateContext())
        {
            var failing = await ctx.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name]).SingleAsync(s => s.Id == first.Id);
            failing.FailureCount = 4;
            failing.LastFailureAt = DateTimeOffset.UtcNow;
            failing.DisabledAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync();
        }

        PushSubscription saved;
        await using (var ctx = CreateContext())
            saved = await new PushSubscriptionRepository(ctx).UpsertByEndpointAsync(
                new PushSubscription { UserId = _user, Endpoint = endpoint, P256dh = "k2", Auth = "a2", DeviceLabel = "Tablet" });

        saved.Id.Should().Be(first.Id);
        await using var read = CreateContext();
        var row = (await read.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(s => s.UserId == _user).ToListAsync()).Should().ContainSingle().Subject;
        row.P256dh.Should().Be("k2");
        row.Auth.Should().Be("a2");
        row.DeviceLabel.Should().Be("Tablet");
        row.FailureCount.Should().Be(0);
        row.LastFailureAt.Should().BeNull();
        row.DisabledAt.Should().BeNull();
    }

    [DockerRequiredFact]
    public async Task Concurrent_registrations_of_one_endpoint_neither_fail_nor_duplicate()
    {
        var registrations = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new PushSubscriptionRepository(ctx).UpsertByEndpointAsync(new PushSubscription
            {
                UserId = _user, Endpoint = "https://fcm.googleapis.com/fcm/send/race", P256dh = $"k{i}", Auth = "a",
            });
        });

        await Task.WhenAll(registrations);

        await using var read = CreateContext();
        (await read.PushSubscriptions.IgnoreQueryFilters([OwnerQueryFilter.Name]).CountAsync(s => s.UserId == _user))
            .Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task Setting_a_watermark_twice_keeps_one_row_holding_the_latest_value()
    {
        var first = DateTimeOffset.UtcNow.AddHours(-2);
        var second = DateTimeOffset.UtcNow.AddHours(-1);
        await using (var ctx = CreateContext())
            await new CompanionCaptureStateRepository(ctx).SetWatermarkAsync("alerts", first);
        await using (var ctx = CreateContext())
            await new CompanionCaptureStateRepository(ctx).SetWatermarkAsync("alerts", second);

        await using var read = CreateContext();
        (await read.CaptureState.CountAsync(s => s.Source == "alerts")).Should().Be(1);
        (await new CompanionCaptureStateRepository(read).GetWatermarkAsync("alerts", DateTimeOffset.MinValue))
            .Should().BeCloseTo(second, TimeSpan.FromMilliseconds(1));
    }

    [DockerRequiredFact]
    public async Task Concurrent_first_watermarks_for_one_source_neither_fail_nor_duplicate()
    {
        var sets = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new CompanionCaptureStateRepository(ctx).SetWatermarkAsync("thesis-breaks", DateTimeOffset.UtcNow.AddMinutes(-i));
        });

        await Task.WhenAll(sets);

        await using var read = CreateContext();
        (await read.CaptureState.CountAsync(s => s.Source == "thesis-breaks")).Should().Be(1);
    }
}
