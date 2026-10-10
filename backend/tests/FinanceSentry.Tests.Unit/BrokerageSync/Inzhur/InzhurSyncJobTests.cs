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
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

/// <summary>Backoff and alerting of the daily read: busy/down is retried by Hangfire after 1 h then 4 h, a dead session asks the owner.</summary>
public class InzhurSyncJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 7, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IInzhurCredentialRepository> _credentials = new(MockBehavior.Loose);
    private readonly Mock<IInzhurSyncService> _sync = new(MockBehavior.Loose);
    private readonly Mock<IBackgroundJobClient> _jobs = new(MockBehavior.Loose);
    private readonly Mock<IAlertGeneratorService> _alerts = new(MockBehavior.Loose);
    private readonly Mock<IUserAlertPreferencesReader> _prefs = new(MockBehavior.Loose);
    private readonly Mock<IConnectionHealthShadow> _health = new(MockBehavior.Loose);
    private readonly List<(Job Job, IState State)> _created = [];
    private readonly Mock<IStorageConnection> _connection = new();
    private readonly InzhurCredential _credential;

    public InzhurSyncJobTests()
    {
        var encryption = new FakeEncryption();
        EncryptedSecret Secret(string s) { var r = encryption.Encrypt(s); return new(r.Ciphertext, r.Iv, r.AuthTag, r.KeyVersion); }
        _credential = new InzhurCredential(_userId, Secret(InzhurFakes.Phone), Secret(InzhurFakes.Password));
        _credential.StartSession(Secret(InzhurFakes.Session().Serialize()), Now.UtcDateTime.AddDays(-2));
        _credential.RecordSyncSuccess(Now.UtcDateTime.AddDays(-1));

        _credentials.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_credential]);
        _credentials.Setup(r => r.GetByUserIdUnscopedAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_credential);
        _prefs.Setup(p => p.GetAsync(It.IsAny<Guid>())).ReturnsAsync(new UserAlertPreferences(true, 0m, true));
        _jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, state) => _created.Add((job, state)))
            .Returns("job-id");
    }

    private InzhurSyncJob Job() => new(
        _credentials.Object, _sync.Object, _jobs.Object, _alerts.Object, _prefs.Object, _health.Object,
        new ManualClock(Now), NullLogger<InzhurSyncJob>.Instance);

    private void SyncFails(InzhurFailureKind kind)
        => _sync.Setup(s => s.SyncAsync(_userId, It.IsAny<CancellationToken>())).ThrowsAsync(new InzhurApiException(kind, "fake failure"));

    private void VerifyAlert(SyncFailureClass failureClass, Times times)
        => _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            _userId, "inzhur", null, null, It.IsAny<string?>(), failureClass, It.IsAny<CancellationToken>()), times);

    private PerformContext Context(int retriesMade)
    {
        _connection.Setup(c => c.GetJobParameter("job-id", "RetryCount")).Returns(retriesMade.ToString());
        var backgroundJob = new BackgroundJob("job-id", Hangfire.Common.Job.FromExpression(() => Console.WriteLine()), DateTime.UtcNow);
        return new PerformContext(Mock.Of<JobStorage>(), _connection.Object, backgroundJob, Mock.Of<IJobCancellationToken>());
    }

    [Fact]
    public async Task A_good_read_resolves_any_open_alert_and_retries_nothing()
    {
        _sync.Setup(s => s.SyncAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(InzhurSyncOutcome.Synced);

        await Job().SyncUserAsync(_userId, Context(0));

        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(_userId, "inzhur", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_daily_run_enqueues_one_read_per_connection()
    {
        await Job().ExecuteAsync();

        var (job, state) = _created.Should().ContainSingle().Subject;
        job.Method.Name.Should().Be(nameof(InzhurSyncJob.SyncUserAsync));
        job.Args[0].Should().Be(_userId);
        state.Should().BeOfType<EnqueuedState>();
        _sync.Verify(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_connection_read_within_the_last_hours_is_not_read_again()
    {
        _credential.RecordSyncSuccess(Now.UtcDateTime.AddHours(-1));

        await Job().ExecuteAsync();

        _created.Should().BeEmpty();
    }

    [Fact]
    public void The_per_user_read_carries_the_one_hour_then_four_hour_retry()
    {
        var retry = typeof(InzhurSyncJob).GetMethod(nameof(InzhurSyncJob.SyncUserAsync))!
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Should().ContainSingle().Subject.Should().BeOfType<AutomaticRetryAttribute>().Subject;

        retry.Attempts.Should().Be(2);
        retry.DelaysInSeconds.Should().Equal(3600, 14400);
        retry.OnAttemptsExceeded.Should().Be(AttemptsExceededAction.Fail);
    }

    [Theory]
    [InlineData(InzhurFailureKind.RateLimited)]
    [InlineData(InzhurFailureKind.Unavailable)]
    public async Task Busy_or_down_is_rethrown_for_hangfire_to_retry_without_alerting(InzhurFailureKind kind)
    {
        SyncFails(kind);

        var act = () => Job().SyncUserAsync(_userId, Context(retriesMade: 0));

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.Kind.Should().Be(kind);
        VerifyAlert(SyncFailureClass.Outage, Times.Never());
    }

    [Fact]
    public async Task The_second_retry_still_waits_without_alerting()
    {
        SyncFails(InzhurFailureKind.Unavailable);

        var act = () => Job().SyncUserAsync(_userId, Context(retriesMade: InzhurSyncJob.RetryAttempts - 1));

        await act.Should().ThrowAsync<InzhurApiException>();
        VerifyAlert(SyncFailureClass.Outage, Times.Never());
    }

    [Fact]
    public async Task Past_the_last_retry_the_day_is_skipped_with_an_outage_alert_and_the_job_fails()
    {
        SyncFails(InzhurFailureKind.Unavailable);

        var act = () => Job().SyncUserAsync(_userId, Context(retriesMade: InzhurSyncJob.RetryAttempts));

        await act.Should().ThrowAsync<InzhurApiException>();
        VerifyAlert(SyncFailureClass.Outage, Times.Once());
    }

    [Fact]
    public async Task A_dead_session_alerts_the_owner_and_is_never_retried()
    {
        SyncFails(InzhurFailureKind.ReauthRequired);

        await Job().SyncUserAsync(_userId, Context(0));

        _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            _userId, "inzhur", null, null, InzhurConnectionStatus.ReauthRequired, SyncFailureClass.Credential, It.IsAny<CancellationToken>()), Times.Once);
        _health.Verify(h => h.RecordFailureAsync(
            It.IsAny<ConnectionHealthSubject>(), It.IsAny<ConnectionHealth>(),
            It.Is<ProviderFailure>(f => f.Class == FailureClass.Credential && f.Strength == FailureStrength.Definitive),
            It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_read_for_a_connection_that_lapsed_meanwhile_does_nothing()
    {
        _credential.MarkReauthRequired("expired");

        await Job().SyncUserAsync(_userId, Context(1));

        _sync.Verify(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
