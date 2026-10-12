using System.Net;
using System.Text.RegularExpressions;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

/// <summary>
/// The session keep-alive, apart from the daily read. A cabinet whose refresh token only survives a few hours without
/// being used is simulated (<see cref="SimulatedCabinet"/>): read once a day it ends in reauth_required, refreshed
/// hourly it never does. A refresh is one POST to the refresh endpoint and nothing else, so it cannot touch any
/// sign-in allowance (there is no sign-in code left: the owner's browser signs in and hands the session over).
/// </summary>
public class InzhurKeepAliveTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 10, 36, 0, TimeSpan.Zero);

    // Measured on the stored session (2026-10-12): the refresh cookie's own Max-Age is 4000 h and the access token
    // lives 15 min; what the auth host enforces behind them cannot be read, so the tests use the 3 h floor.
    private static readonly TimeSpan IdleLimit = TimeSpan.FromHours(3);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeEncryption _encryption = new();
    private readonly ManualClock _clock = new(Start);
    private readonly SimulatedCabinet _cabinet;
    private readonly FakeSessionGate _gate = new();
    private readonly Mock<IInzhurCredentialRepository> _credentials = new(MockBehavior.Loose);
    private readonly Mock<IBrokerageHoldingRepository> _holdings = new(MockBehavior.Loose);
    private readonly InzhurCredential _credential;
    private readonly List<int> _gateHoldersAtLoad = [];

    public InzhurKeepAliveTests()
    {
        _cabinet = new SimulatedCabinet(_clock, IdleLimit, _gate);
        _credential = new InzhurCredential(_userId);
        _credential.StartSession(Secret(InzhurFakes.Session(refreshValue: _cabinet.Issue()).Serialize()), Start.UtcDateTime);

        _credentials.Setup(r => r.GetFreshByUserIdUnscopedAsync(_userId, It.IsAny<CancellationToken>()))
            .Callback(() => _gateHoldersAtLoad.Add(_gate.Holders))
            .ReturnsAsync(_credential);
        _holdings.Setup(h => h.GetByUserIdUnscopedAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<BrokerageHolding>());
    }

    private EncryptedSecret Secret(string plaintext)
    {
        var r = _encryption.Encrypt(plaintext);
        return new EncryptedSecret(r.Ciphertext, r.Iv, r.AuthTag, r.KeyVersion);
    }

    private InzhurApiClient Api() => new(
        new HttpClient(_cabinet), Options.Create(new InzhurOptions()), NullLogger<InzhurApiClient>.Instance);

    private InzhurKeepAliveService KeepAlive() => new(
        _credentials.Object, Api(), _gate, _encryption, _clock, NullLogger<InzhurKeepAliveService>.Instance);

    private InzhurSyncService DailyRead() => new(
        _credentials.Object, _holdings.Object, Api(), _gate, _encryption, _clock, NullLogger<InzhurSyncService>.Instance);

    // -- the reproduction ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Read_only_once_a_day_the_session_dies_between_runs_and_the_connection_asks_for_a_reconnect()
    {
        _clock.Now = Start.AddHours(24);

        var act = () => DailyRead().SyncAsync(_userId);

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.Kind.Should().Be(InzhurFailureKind.ReauthRequired);
        _credential.Status.Should().Be(InzhurConnectionStatus.ReauthRequired);
        _credential.HasSession.Should().BeFalse();
    }

    [Fact]
    public async Task Refreshed_hourly_the_same_session_is_still_alive_for_the_next_daily_read()
    {
        var options = new InzhurOptions();
        for (var elapsed = options.KeepAliveInterval; elapsed <= TimeSpan.FromHours(24); elapsed += options.KeepAliveInterval)
        {
            _clock.Now = Start + elapsed;
            (await KeepAlive().KeepAliveAsync(_userId)).Should().Be(InzhurKeepAliveOutcome.Refreshed);
        }

        _clock.Now = Start.AddHours(24).AddMinutes(30);
        (await DailyRead().SyncAsync(_userId)).Should().Be(InzhurSyncOutcome.Synced);

        _credential.Status.Should().Be(InzhurConnectionStatus.Active);
        _cabinet.LongestGapBetweenRefreshes.Should().BeLessThan(IdleLimit);
    }

    [Fact]
    public async Task The_rotated_jar_of_every_refresh_is_what_the_next_one_presents()
    {
        // The simulated cabinet rotates the refresh token on every call and rejects the one it replaced.
        for (var run = 1; run <= 5; run++)
        {
            _clock.Now = Start.AddHours(run);
            await KeepAlive().KeepAliveAsync(_userId);
        }

        _cabinet.Rejected.Should().Be(0);
        _credential.SessionRefreshedAt.Should().Be(Start.AddHours(5).UtcDateTime);
        _credential.SessionStartedAt.Should().Be(Start.UtcDateTime, "a refresh continues the session; it does not start one");
    }

    // -- a refresh is not a sign-in --------------------------------------------------------------------------------

    [Fact]
    public async Task A_day_of_keep_alive_is_24_refresh_posts_and_no_other_call()
    {
        for (var hour = 1; hour <= 24; hour++)
        {
            _clock.Now = Start.AddHours(hour);
            await KeepAlive().KeepAliveAsync(_userId);
        }

        _cabinet.Requests.Should().HaveCount(24);
        _cabinet.Requests.Select(r => $"{r.Method} {r.Uri.AbsolutePath}").Distinct().Should().Equal(
            "POST /auth/api/v1/auth/refresh");
        _cabinet.Requests.Should().OnlyContain(r => r.Body == string.Empty || r.Body == null,
            "a refresh sends the cookie jar, never credentials");
    }

    [Fact]
    public async Task Keep_alive_reads_no_portfolio_and_stores_no_holdings()
    {
        await KeepAlive().KeepAliveAsync(_userId);

        _holdings.Invocations.Should().BeEmpty();
        _credential.LastSyncAt.Should().BeNull();
    }

    // -- failures --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_busy_or_down_cabinet_leaves_the_connection_exactly_as_it_was(HttpStatusCode status)
    {
        _cabinet.FailNextWith(status);
        var refreshedBefore = _credential.SessionRefreshedAt;
        var sessionBefore = _credential.EncryptedSession;
        _clock.Now = Start.AddHours(1);

        var act = () => KeepAlive().KeepAliveAsync(_userId);

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.IsTransient.Should().BeTrue();
        _credential.Status.Should().Be(InzhurConnectionStatus.Active);
        _credential.HasSession.Should().BeTrue();
        _credential.LastSyncError.Should().BeNull("the keep-alive does not record sync errors");
        _credential.SessionRefreshedAt.Should().Be(refreshedBefore);
        _credential.EncryptedSession.Should().BeSameAs(sessionBefore);
        _credentials.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_network_failure_is_transient_and_does_not_end_the_session()
    {
        _cabinet.ThrowNetworkError();
        _clock.Now = Start.AddHours(1);

        var act = () => KeepAlive().KeepAliveAsync(_userId);

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.IsTransient.Should().BeTrue();
        _credential.Status.Should().Be(InzhurConnectionStatus.Active);
        _credential.HasSession.Should().BeTrue();
    }

    [Fact]
    public async Task A_refresh_the_cabinet_rejects_flips_the_connection_and_drops_the_session()
    {
        _clock.Now = Start.AddHours(4);

        var act = () => KeepAlive().KeepAliveAsync(_userId);

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.Kind.Should().Be(InzhurFailureKind.ReauthRequired);
        _credential.Status.Should().Be(InzhurConnectionStatus.ReauthRequired);
        _credential.HasSession.Should().BeFalse();
        _credentials.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_connection_waiting_for_the_owner_or_without_a_session_is_skipped_without_a_call()
    {
        _credential.MarkReauthRequired("expired");

        (await KeepAlive().KeepAliveAsync(_userId)).Should().Be(InzhurKeepAliveOutcome.Skipped);

        _credentials.Setup(r => r.GetFreshByUserIdUnscopedAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InzhurCredential?)null);
        (await KeepAlive().KeepAliveAsync(_userId)).Should().Be(InzhurKeepAliveOutcome.Skipped);
        _cabinet.Requests.Should().BeEmpty();
    }

    // -- serialization with the daily read ------------------------------------------------------------------------

    [Fact]
    public async Task The_jar_is_read_and_refreshed_only_while_the_users_turn_is_held()
    {
        _clock.Now = Start.AddHours(1);

        await KeepAlive().KeepAliveAsync(_userId);
        await DailyRead().SyncAsync(_userId);

        _gateHoldersAtLoad.Should().Equal(
            [1, 1],
            "the credential is loaded after the turn is taken: whoever held it before may have rotated the token");
        _cabinet.RequestsOutsideTheTurn.Should().Be(0);
        _gate.Holders.Should().Be(0, "the turn is released when the call ends, failed or not");
    }

    [Fact]
    public async Task The_keep_alive_and_the_daily_read_of_one_user_never_overlap()
    {
        _cabinet.SlowDown(TimeSpan.FromMilliseconds(30));
        _clock.Now = Start.AddHours(1);

        await Task.WhenAll(
            KeepAlive().KeepAliveAsync(_userId),
            DailyRead().SyncAsync(_userId),
            KeepAlive().KeepAliveAsync(_userId));

        _gate.MaxConcurrent.Should().Be(1);
        _cabinet.Rejected.Should().Be(0, "no call presented a refresh token another call had already replaced");
    }

    [Fact]
    public async Task The_gate_is_released_when_the_refresh_fails()
    {
        _cabinet.FailNextWith(HttpStatusCode.ServiceUnavailable);

        var act = () => KeepAlive().KeepAliveAsync(_userId);

        await act.Should().ThrowAsync<InzhurApiException>();
        _gate.Holders.Should().Be(0);
    }

    // -- cadence derived from the lifetime ------------------------------------------------------------------------

    [Fact]
    public void The_default_cadence_is_hourly_from_the_3_hour_floor()
    {
        var options = new InzhurOptions();

        options.KeepAliveInterval.Should().Be(TimeSpan.FromHours(1));
        options.KeepAliveCron.Should().Be("17 * * * *");
        options.SyncCron.Should().Be("0 7 * * *", "the holdings read stays once a day");
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(6, 3)]
    [InlineData(9, 2)]
    [InlineData(24, 3)]
    public void The_cron_never_leaves_a_longer_gap_than_the_derived_interval(int lifetimeHours, int runsPerLifetime)
    {
        var options = new InzhurOptions
        {
            SessionLifetimeFloor = TimeSpan.FromHours(lifetimeHours),
            KeepAliveRunsPerLifetime = runsPerLifetime,
        };

        var longestGap = LongestGap(options.KeepAliveCron, days: 3);

        options.KeepAliveInterval.Should().BeLessThanOrEqualTo(options.SessionLifetimeFloor);
        longestGap.Should().BeLessThanOrEqualTo(options.KeepAliveInterval);
    }

    [Theory]
    [InlineData(30, 3, 15)]
    [InlineData(10, 3, 15)]
    [InlineData(180, 3, 60)]
    [InlineData(180, 1, 180)]
    [InlineData(48 * 60, 1, 12 * 60)]
    public void The_interval_is_the_lifetime_over_the_runs_within_its_bounds(int lifetimeMinutes, int runs, int expectedMinutes)
    {
        InzhurKeepAliveSchedule.IntervalFor(TimeSpan.FromMinutes(lifetimeMinutes), runs)
            .Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void A_sub_hour_interval_gets_a_minute_cron()
    {
        new InzhurOptions { SessionLifetimeFloor = TimeSpan.FromMinutes(45) }.KeepAliveCron.Should().Be("*/15 * * * *");
    }

    // Hangfire's cron parser is internal, and the schedule only ever produces three shapes: "M * * * *",
    // "M */H * * *" and "*/M * * * *".
    private static TimeSpan LongestGap(string cron, int days)
    {
        var fields = cron.Split(' ');
        fields.Should().HaveCount(5);
        var minutes = Expand(fields[0], 60);
        var hours = Expand(fields[1], 24);

        var fires = Enumerable.Range(0, days)
            .SelectMany(day => hours.SelectMany(h => minutes.Select(m => day * 24 * 60 + h * 60 + m)))
            .Order()
            .ToList();
        fires.Should().HaveCountGreaterThan(1);
        return TimeSpan.FromMinutes(fires.Zip(fires.Skip(1), (a, b) => b - a).Max());
    }

    private static List<int> Expand(string field, int count)
    {
        if (field == "*")
            return Enumerable.Range(0, count).ToList();
        if (field.StartsWith("*/", StringComparison.Ordinal))
            return Enumerable.Range(0, count).Where(v => v % int.Parse(field[2..]) == 0).ToList();
        return [int.Parse(field)];
    }

    // -- the job ---------------------------------------------------------------------------------------------------

    public sealed class JobTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<IInzhurCredentialRepository> _credentials = new(MockBehavior.Loose);
        private readonly Mock<IInzhurKeepAliveService> _keepAlive = new(MockBehavior.Loose);
        private readonly Mock<IBackgroundJobClient> _jobs = new(MockBehavior.Loose);
        private readonly Mock<IAlertGeneratorService> _alerts = new(MockBehavior.Loose);
        private readonly Mock<IUserAlertPreferencesReader> _prefs = new(MockBehavior.Loose);
        private readonly Mock<IConnectionHealthShadow> _health = new(MockBehavior.Loose);
        private readonly List<(Job Job, IState State)> _created = [];
        private readonly InzhurCredential _credential;

        public JobTests()
        {
            var encryption = new FakeEncryption();
            var secret = encryption.Encrypt(InzhurFakes.Session().Serialize());
            _credential = new InzhurCredential(_userId);
            _credential.StartSession(new EncryptedSecret(secret.Ciphertext, secret.Iv, secret.AuthTag, secret.KeyVersion), Now.UtcDateTime.AddDays(-2));
            _credential.RefreshSession(new EncryptedSecret(secret.Ciphertext, secret.Iv, secret.AuthTag, secret.KeyVersion), Now.UtcDateTime.AddHours(-1));

            _credentials.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_credential]);
            _credentials.Setup(r => r.GetByUserIdUnscopedAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_credential);
            _prefs.Setup(p => p.GetAsync(It.IsAny<Guid>())).ReturnsAsync(new UserAlertPreferences(true, 0m, true));
            _jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
                .Callback<Job, IState>((job, state) => _created.Add((job, state)))
                .Returns("job-id");
        }

        private InzhurKeepAliveJob Job() => new(
            _credentials.Object, _keepAlive.Object, _jobs.Object, _alerts.Object, _prefs.Object, _health.Object,
            Options.Create(new InzhurOptions()), new ManualClock(Now), NullLogger<InzhurKeepAliveJob>.Instance);

        private void KeepAliveFails(InzhurFailureKind kind)
            => _keepAlive.Setup(s => s.KeepAliveAsync(_userId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InzhurApiException(kind, "fake failure"));

        [Fact]
        public async Task Each_run_enqueues_one_refresh_per_connection_that_has_not_just_been_refreshed()
        {
            await Job().ExecuteAsync();

            var (job, state) = _created.Should().ContainSingle().Subject;
            job.Method.Name.Should().Be(nameof(InzhurKeepAliveJob.KeepAliveUserAsync));
            job.Args[0].Should().Be(_userId);
            state.Should().BeOfType<EnqueuedState>();
            _keepAlive.Verify(s => s.KeepAliveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_session_refreshed_minutes_ago_by_the_daily_read_or_a_connect_is_left_for_the_next_run()
        {
            _credential.RefreshSession(
                new EncryptedSecret([1], [2], [3], 1), Now.UtcDateTime.AddMinutes(-10));

            await Job().ExecuteAsync();

            _created.Should().BeEmpty();
        }

        [Fact]
        public async Task A_good_refresh_alerts_and_changes_nothing()
        {
            _keepAlive.Setup(s => s.KeepAliveAsync(_userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(InzhurKeepAliveOutcome.Refreshed);

            await Job().KeepAliveUserAsync(_userId);

            _alerts.Invocations.Should().BeEmpty();
            _health.Invocations.Should().BeEmpty();
        }

        [Theory]
        [InlineData(InzhurFailureKind.RateLimited)]
        [InlineData(InzhurFailureKind.Unavailable)]
        public async Task A_busy_or_down_cabinet_is_not_an_alert_nor_a_health_failure_and_the_next_run_retries(InzhurFailureKind kind)
        {
            KeepAliveFails(kind);

            await Job().KeepAliveUserAsync(_userId);

            _alerts.Invocations.Should().BeEmpty();
            _health.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task A_dead_session_alerts_the_owner_and_records_a_definitive_credential_failure()
        {
            KeepAliveFails(InzhurFailureKind.ReauthRequired);

            await Job().KeepAliveUserAsync(_userId);

            _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
                _userId, "inzhur", null, null, InzhurConnectionStatus.ReauthRequired, SyncFailureClass.Credential, It.IsAny<CancellationToken>()), Times.Once);
            _health.Verify(h => h.RecordFailureAsync(
                It.IsAny<ConnectionHealthSubject>(), It.IsAny<ConnectionHealth>(),
                It.Is<ProviderFailure>(f => f.Class == FailureClass.Credential && f.Strength == FailureStrength.Definitive),
                It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_dead_session_stays_quiet_for_an_owner_who_turned_sync_failure_alerts_off()
        {
            _prefs.Setup(p => p.GetAsync(It.IsAny<Guid>())).ReturnsAsync(new UserAlertPreferences(true, 0m, false));
            KeepAliveFails(InzhurFailureKind.ReauthRequired);

            await Job().KeepAliveUserAsync(_userId);

            _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task An_unexpected_cabinet_answer_is_not_swallowed()
        {
            KeepAliveFails(InzhurFailureKind.Unexpected);

            var act = () => Job().KeepAliveUserAsync(_userId);

            await act.Should().ThrowAsync<InzhurApiException>();
        }

        [Fact]
        public void The_keep_alive_is_registered_apart_from_the_daily_read_and_is_not_retried_by_hangfire()
        {
            typeof(InzhurKeepAliveJob).GetMethod(nameof(InzhurKeepAliveJob.KeepAliveUserAsync))!
                .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
                .Should().ContainSingle().Which.Should().BeOfType<AutomaticRetryAttribute>()
                .Which.Attempts.Should().Be(0, "the next scheduled run is the retry");
        }
    }

    // -- the per-user gate -----------------------------------------------------------------------------------------

    public sealed class GateTests
    {
        private readonly Mock<JobStorage> _storage = new();
        private readonly Mock<IStorageConnection> _connection = new();
        private readonly Mock<IDisposable> _lock = new();

        public GateTests()
        {
            _storage.Setup(s => s.GetConnection()).Returns(_connection.Object);
            _connection.Setup(c => c.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns(_lock.Object);
        }

        [Fact]
        public async Task The_turn_is_a_distributed_lock_per_user_released_with_the_connection()
        {
            var user = Guid.NewGuid();
            var gate = new HangfireInzhurSessionGate(() => _storage.Object);

            var turn = await gate.EnterAsync(user);

            _connection.Verify(c => c.AcquireDistributedLock(
                HangfireInzhurSessionGate.ResourceFor(user), HangfireInzhurSessionGate.WaitLimit), Times.Once);
            _lock.Verify(l => l.Dispose(), Times.Never);

            turn.Dispose();

            _lock.Verify(l => l.Dispose(), Times.Once);
            _connection.Verify(c => c.Dispose(), Times.Once);
        }

        [Fact]
        public void Two_users_do_not_share_a_turn()
        {
            HangfireInzhurSessionGate.ResourceFor(Guid.NewGuid()).Should().NotBe(HangfireInzhurSessionGate.ResourceFor(Guid.NewGuid()));
        }

        [Fact]
        public async Task A_turn_that_does_not_come_is_a_transient_failure_for_the_next_run_to_retry()
        {
            _connection.Setup(c => c.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .Throws(new DistributedLockTimeoutException("inzhur-session"));

            var act = () => new HangfireInzhurSessionGate(() => _storage.Object).EnterAsync(Guid.NewGuid());

            (await act.Should().ThrowAsync<InzhurApiException>()).Which.IsTransient.Should().BeTrue();
            _connection.Verify(c => c.Dispose(), Times.Once);
        }
    }

    // -- fakes -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// An Inzhur whose refresh token dies when it sits unused longer than the idle limit, and is replaced on every use
    /// (the one it replaced is rejected). Every answer is derived from the shared clock.
    /// </summary>
    private sealed class SimulatedCabinet : HttpMessageHandler
    {
        private static readonly Regex RefreshCookie = new($"{InzhurFakes.RefreshCookieName}=([^;]+)", RegexOptions.Compiled);

        private readonly ManualClock _clock;
        private readonly TimeSpan _idleLimit;
        private readonly FakeSessionGate _gate;
        private readonly object _sync = new();
        private readonly Queue<Func<HttpResponseMessage>> _next = new();
        private string _current;
        private DateTimeOffset _lastUsed;
        private DateTimeOffset? _lastRefreshAt;
        private int _issued;
        private TimeSpan _delay = TimeSpan.Zero;

        public SimulatedCabinet(ManualClock clock, TimeSpan idleLimit, FakeSessionGate gate)
        {
            _clock = clock;
            _idleLimit = idleLimit;
            _gate = gate;
            _lastUsed = clock.Now;
            _current = $"fake-refresh-{++_issued}";
        }

        public List<RecordedRequest> Requests { get; } = [];

        public int Rejected { get; private set; }

        public int RequestsOutsideTheTurn { get; private set; }

        public TimeSpan LongestGapBetweenRefreshes { get; private set; }

        /// <summary>The refresh token the cabinet currently accepts (what the owner pasted at connect time).</summary>
        public string Issue() => _current;

        public void FailNextWith(HttpStatusCode status) => _next.Enqueue(() => InzhurFakes.Error(status, "maintenance"));

        public void ThrowNetworkError() => _next.Enqueue(() => throw new HttpRequestException("connection reset"));

        public void SlowDown(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("Cookie", out var cookies);
            lock (_sync)
            {
                Requests.Add(new RecordedRequest(
                    request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
                    cookies is null ? null : string.Join("; ", cookies), body));
                if (_gate.Holders == 0)
                    RequestsOutsideTheTurn++;
            }

            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, cancellationToken);

            lock (_sync)
            {
                if (_next.Count > 0)
                    return _next.Dequeue()();

                return request.RequestUri!.AbsolutePath switch
                {
                    "/auth/api/v1/auth/refresh" => Refresh(request),
                    "/core/api/v1/user-assets" => InzhurFakes.Json(HttpStatusCode.OK, InzhurFakes.UserAssets(InzhurFakes.Fund("Fund A", 10m, 1_200m, 1_000m))),
                    "/core/api/v1/users/broker-account" => InzhurFakes.Json(HttpStatusCode.OK, InzhurFakes.BrokerAccount(500m, 0m)),
                    _ => throw new InvalidOperationException($"Unexpected Inzhur call {request.Method} {request.RequestUri}."),
                };
            }
        }

        private HttpResponseMessage Refresh(HttpRequestMessage request)
        {
            var now = _clock.Now;
            var presented = request.Headers.TryGetValues("Cookie", out var cookies)
                ? RefreshCookie.Match(string.Join("; ", cookies)) is { Success: true } m ? m.Groups[1].Value : null
                : null;

            if (presented != _current || now - _lastUsed > _idleLimit)
            {
                Rejected++;
                return InzhurFakes.Error(HttpStatusCode.Unauthorized, "Invalid or expired refresh token");
            }

            if (_lastRefreshAt is { } last && now - last > LongestGapBetweenRefreshes)
                LongestGapBetweenRefreshes = now - last;
            _lastRefreshAt = now;
            _lastUsed = now;
            _current = $"fake-refresh-{++_issued}";

            var response = InzhurFakes.Json(HttpStatusCode.OK, new { accessToken = $"fake-access-{_issued}" });
            response.Headers.Add("Set-Cookie", $"{InzhurFakes.RefreshCookieName}={_current}; Domain={InzhurFakes.AuthHost}; Path=/; Secure; HttpOnly");
            return response;
        }
    }
}
