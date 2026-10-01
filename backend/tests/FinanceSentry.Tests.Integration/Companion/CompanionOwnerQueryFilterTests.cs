namespace FinanceSentry.Tests.Integration.Companion;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.EventHandlers;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Jobs;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="CompanionDbContext"/>: a context acting for one person sees only that
/// person's notification setting and outbox events, a context with no person in scope sees none, and the
/// capture, dispatch and digest jobs and the registration handler (which run with no person in scope) opt out
/// explicitly, so they still see the users they name. Real Postgres, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CompanionOwnerQueryFilterTests : IAsyncLifetime
{
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private readonly MaterialityPolicy _policy = new();
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    // Null acts as a background job: no person in scope.
    private CompanionDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<CompanionDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options,
            new FixedCurrentUser(actingUser));

    private static NotificationSettingRepository Settings(CompanionDbContext ctx) =>
        new(ctx, Options.Create(new CompanionOptions()));

    private static CompanionNotificationSetting NewSetting(Guid userId, NotificationMode mode) => new()
    {
        UserId = userId,
        Mode = mode,
        TimeZoneId = "UTC",
        MaxProactivePerHour = 1,
        DigestHourLocal = DateTimeOffset.UtcNow.Hour,
    };

    private static CompanionEvent NewEvent(Guid userId, EventDisposition disposition, DateTimeOffset? dispatchedAt = null) => new()
    {
        UserId = userId,
        Kind = CompanionEventKind.NewsCluster,
        Subject = "MU",
        Severity = "info",
        Summary = "News cluster: MU",
        DedupKey = $"test:{Guid.NewGuid():N}",
        SourceModule = "alerts",
        Disposition = disposition,
        OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        DispatchedAt = dispatchedAt,
    };

    private async Task SeedAsync(params object[] entities)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    [Fact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes().Where(e => e.FindProperty("UserId") is not null).ToList();

        perUser.Should().HaveCount(2);
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_rows_and_no_person_sees_none()
    {
        await SeedAsync(
            NewSetting(_userA, NotificationMode.Scan), NewSetting(_userB, NotificationMode.Scan),
            NewEvent(_userA, EventDisposition.Pending), NewEvent(_userB, EventDisposition.Pending));

        await using (var asA = CreateContext(_userA))
        {
            (await asA.NotificationSettings.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);
            (await asA.Events.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);
        }

        await using (var asB = CreateContext(_userB))
        {
            (await asB.NotificationSettings.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
            (await asB.Events.AnyAsync(x => x.UserId == _userA)).Should().BeFalse();
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.NotificationSettings.AnyAsync()).Should().BeFalse("no person in scope matches no row");
        (await asNoOne.Events.AnyAsync()).Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Repository_reads_follow_the_acting_person_and_the_unscoped_reads_serve_callers_without_one()
    {
        var eventA = NewEvent(_userA, EventDisposition.Pending);
        var eventB = NewEvent(_userB, EventDisposition.Pending);
        await SeedAsync(NewSetting(_userA, NotificationMode.Realtime), eventA, eventB);

        await using (var asA = CreateContext(_userA))
        {
            (await Settings(asA).GetOrDefaultAsync(_userA)).Mode.Should().Be(NotificationMode.Realtime);
            (await new CompanionEventRepository(asA).GetAsync(eventA.Id)).Should().NotBeNull();
            (await new CompanionEventRepository(asA).GetAsync(eventB.Id)).Should().BeNull(
                "naming another person's row does not lift the owner scope");
        }

        await using var asNoOne = CreateContext();
        (await Settings(asNoOne).GetOrDefaultAsync(_userA)).Mode.Should().Be(NotificationMode.Scan,
            "with no person in scope the filtered read finds nothing and falls back to the default");
        (await Settings(asNoOne).GetOrDefaultUnscopedAsync(_userA)).Mode.Should().Be(NotificationMode.Realtime);
        (await new CompanionEventRepository(asNoOne).ListRealtimePendingUnscopedAsync(10))
            .Select(e => e.Id).Should().BeEquivalentTo([eventA.Id, eventB.Id]);
    }

    [DockerRequiredFact]
    public async Task Capture_with_no_person_reads_the_users_mode_and_skips_an_already_captured_alert()
    {
        await SeedAsync(NewSetting(_userA, NotificationMode.Digest));
        var alert = new MaterialAlertRecord(
            Guid.NewGuid(), _userA, "NewsCluster", "info", "News cluster: MU", null, "MU", DateTimeOffset.UtcNow);
        var alerts = new Mock<IMaterialAlertReader>();
        alerts.Setup(a => a.GetNewSinceAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([alert]);
        var analyst = new Mock<IAnalystActionFeedReader>();
        analyst.Setup(a => a.GetNewSinceAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        async Task<int> CaptureAsync()
        {
            await using var ctx = CreateContext();
            var capture = new CompanionEventCapture(
                alerts.Object, analyst.Object, Mock.Of<IBrokerageHoldingsReader>(), Mock.Of<IBankingTotalsReader>(),
                Mock.Of<IBankingAccountsReader>(), Settings(ctx), new CompanionEventRepository(ctx),
                new CompanionCaptureStateRepository(ctx), _policy, Options.Create(new CompanionOptions()),
                NullLogger<CompanionEventCapture>.Instance);
            return await capture.CaptureAsync();
        }

        (await CaptureAsync()).Should().Be(1);
        (await CaptureAsync()).Should().Be(0, "the dedup check sees the row the first run wrote");

        await using var asA = CreateContext(_userA);
        var captured = await asA.Events.SingleAsync();
        captured.Disposition.Should().Be(EventDisposition.HeldForDigest,
            "the capture read the user's saved digest mode, not the unsaved scan default");
    }

    [DockerRequiredFact]
    public async Task Dispatch_with_no_person_applies_each_users_setting_and_rate_limit()
    {
        var pendingA = NewEvent(_userA, EventDisposition.Pending);
        var pendingB = NewEvent(_userB, EventDisposition.Pending);
        await SeedAsync(
            NewSetting(_userA, NotificationMode.Realtime),
            NewEvent(_userA, EventDisposition.Dispatched, DateTimeOffset.UtcNow.AddMinutes(-10)),
            pendingA,
            pendingB);
        var dispatcher = new Mock<IAgentWakeDispatcher>();
        var authorization = new Mock<IUserAuthorizationChecker>();
        authorization.Setup(a => a.IsAuthorizedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await using (var ctx = CreateContext())
        {
            await new CompanionDispatchJob(
                new CompanionEventRepository(ctx), Settings(ctx), dispatcher.Object, authorization.Object,
                Options.Create(new CompanionOptions()), NullLogger<CompanionDispatchJob>.Instance).ExecuteAsync();
        }

        dispatcher.Verify(d => d.WakeAsync(It.IsAny<CompanionEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        await using (var asA = CreateContext(_userA))
        {
            (await asA.Events.SingleAsync(e => e.Id == pendingA.Id)).Disposition.Should().Be(
                EventDisposition.SuppressedByRateLimit,
                "the job read A's realtime setting and counted A's dispatch in the last hour");
        }

        await using var asB = CreateContext(_userB);
        (await asB.Events.SingleAsync()).Disposition.Should().Be(EventDisposition.Pending,
            "B has no saved setting, so the scan default leaves the event for the pull path");
    }

    [DockerRequiredFact]
    public async Task Digest_with_no_person_wakes_the_user_holding_events_at_their_digest_hour()
    {
        await SeedAsync(
            NewSetting(_userA, NotificationMode.Digest),
            NewEvent(_userA, EventDisposition.HeldForDigest),
            NewEvent(_userA, EventDisposition.HeldForDigest));
        var dispatcher = new Mock<IAgentWakeDispatcher>();
        dispatcher.Setup(d => d.WakeDigestAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WakeResult.Sent);

        await using var ctx = CreateContext();
        await new CompanionDigestJob(
            Settings(ctx), new CompanionEventRepository(ctx), dispatcher.Object,
            NullLogger<CompanionDigestJob>.Instance).ExecuteAsync();

        dispatcher.Verify(d => d.WakeDigestAsync(_userA, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [DockerRequiredFact]
    public async Task Registration_with_no_person_provisions_one_setting_row_and_keeps_an_existing_one()
    {
        await SeedAsync(NewSetting(_userA, NotificationMode.Realtime));

        await using (var ctx = CreateContext())
        {
            var handler = new UserRegisteredSettingsProvisioningHandler(Settings(ctx));
            await handler.Handle(new UserRegisteredEvent(_userA), default);
            await handler.Handle(new UserRegisteredEvent(_userB), default);
        }

        await using (var asA = CreateContext(_userA))
        {
            (await asA.NotificationSettings.SingleAsync()).Mode.Should().Be(NotificationMode.Realtime,
                "the existence check found A's row instead of inserting a duplicate default");
        }

        await using var asB = CreateContext(_userB);
        (await asB.NotificationSettings.SingleAsync()).Mode.Should().Be(NotificationMode.Scan);
    }
}
