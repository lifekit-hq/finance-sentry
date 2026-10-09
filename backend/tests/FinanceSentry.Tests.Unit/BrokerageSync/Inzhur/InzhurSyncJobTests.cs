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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

/// <summary>Backoff and alerting of the daily read: busy/down retries after 1 h then 4 h, a dead session asks the owner.</summary>
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
    private readonly List<(Job Job, IState State)> _scheduled = [];
    private readonly InzhurCredential _credential;

    public InzhurSyncJobTests()
    {
        var encryption = new FakeEncryption();
        EncryptedSecret Secret(string s) { var r = encryption.Encrypt(s); return new(r.Ciphertext, r.Iv, r.AuthTag, r.KeyVersion); }
        _credential = new InzhurCredential(_userId);
        _credential.StartSession(Secret(InzhurFakes.Session().Serialize()), Now.UtcDateTime.AddDays(-2));
        _credential.RecordSyncSuccess(Now.UtcDateTime.AddDays(-1));

        _credentials.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_credential]);
        _credentials.Setup(r => r.GetByUserIdUnscopedAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_credential);
        _prefs.Setup(p => p.GetAsync(It.IsAny<Guid>())).ReturnsAsync(new UserAlertPreferences(true, 0m, true));
        _jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, state) => _scheduled.Add((job, state)))
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

    [Fact]
    public async Task A_good_read_resolves_any_open_alert_and_schedules_nothing()
    {
        _sync.Setup(s => s.SyncAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(InzhurSyncOutcome.Synced);

        await Job().ExecuteAsync();

        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(_userId, "inzhur", null, It.IsAny<CancellationToken>()), Times.Once);
        _scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task A_connection_read_within_the_last_hours_is_not_read_again()
    {
        _credential.RecordSyncSuccess(Now.UtcDateTime.AddHours(-1));

        await Job().ExecuteAsync();

        _sync.Verify(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(InzhurFailureKind.RateLimited)]
    [InlineData(InzhurFailureKind.Unavailable)]
    public async Task Busy_or_down_retries_after_an_hour_without_alerting(InzhurFailureKind kind)
    {
        SyncFails(kind);

        await Job().ExecuteAsync();

        var (job, state) = _scheduled.Should().ContainSingle().Subject;
        job.Method.Name.Should().Be(nameof(InzhurSyncJob.RetryAsync));
        job.Args.Should().Equal(_userId, 1);
        state.Should().BeOfType<ScheduledState>().Which.EnqueueAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));
        VerifyAlert(SyncFailureClass.Outage, Times.Never());
    }

    [Fact]
    public async Task The_second_retry_waits_four_hours()
    {
        SyncFails(InzhurFailureKind.Unavailable);

        await Job().RetryAsync(_userId, 1);

        var (job, state) = _scheduled.Should().ContainSingle().Subject;
        job.Args.Should().Equal(_userId, 2);
        state.Should().BeOfType<ScheduledState>().Which.EnqueueAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(4), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Past_the_last_retry_the_day_is_skipped_with_an_outage_alert()
    {
        SyncFails(InzhurFailureKind.Unavailable);

        await Job().RetryAsync(_userId, InzhurSyncJob.RetryDelays.Count);

        _scheduled.Should().BeEmpty();
        VerifyAlert(SyncFailureClass.Outage, Times.Once());
    }

    [Fact]
    public async Task A_dead_session_alerts_the_owner_and_is_never_retried()
    {
        SyncFails(InzhurFailureKind.ReauthRequired);

        await Job().ExecuteAsync();

        _scheduled.Should().BeEmpty();
        _alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
            _userId, "inzhur", null, null, InzhurConnectionStatus.ReauthRequired, SyncFailureClass.Credential, It.IsAny<CancellationToken>()), Times.Once);
        _health.Verify(h => h.RecordFailureAsync(
            It.IsAny<ConnectionHealthSubject>(), It.IsAny<ConnectionHealth>(),
            It.Is<ProviderFailure>(f => f.Class == FailureClass.Credential && f.Strength == FailureStrength.Definitive),
            It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_retry_for_a_connection_that_lapsed_meanwhile_does_nothing()
    {
        _credential.MarkReauthRequired("expired");

        await Job().RetryAsync(_userId, 1);

        _sync.Verify(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
