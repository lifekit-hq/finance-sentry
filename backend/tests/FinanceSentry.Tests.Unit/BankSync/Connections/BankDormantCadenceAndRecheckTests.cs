namespace FinanceSentry.Tests.Unit.BankSync.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Connections;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// Slice S4 on the bank scheduler: a Dormant account or connection of an active provider syncs once a day, and a
/// scheduled sync that raises a suspect credential signal on its connection is looked at again a minute on (never
/// inside Monobank's one-call-a-minute limit) and once more when it can be confirmed. A shadow provider's schedule is
/// untouched. The coordinator runs over the real policy; only the sync itself is faked.
/// </summary>
public class BankDormantCadenceAndRecheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly Mock<IMonobankCredentialRepository> _credentials = new();
    private readonly Mock<ITrueLayerConnectionRepository> _connections = new();
    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<IScheduledSyncService> _syncService = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobs = new();
    private readonly List<(Job Job, IState State)> _scheduled = [];
    private readonly Guid _credentialId = Guid.NewGuid();
    private readonly ConnectionHealthOptions _options = HealthOptions.With("monobank");
    private readonly BankAccount _account;
    private ConnectionHealth _credentialHealth = new();

    public BankDormantCadenceAndRecheckTests()
    {
        _account = new BankAccount
        {
            Provider = MonobankFailureClassifier.ProviderName,
            MonobankCredentialId = _credentialId,
            UserId = Guid.NewGuid(),
            BankName = "Monobank",
        };
        ConnectionHealthStores.Back(_credentials, _credentialId, () => _credentialHealth, h => _credentialHealth = h);
        _accounts.Setup(r => r.GetByIdUnscopedAsync(_account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_account);
        _backgroundJobs
            .Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, state) => _scheduled.Add((job, state)))
            .Returns("job-id");
    }

    private BankConnectionHealth CreateHealth() =>
        BankConnectionHealths.Create(
            _accounts.Object, _credentials.Object, _connections.Object,
            Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(), _time, _options);

    private TransactionSyncCoordinator CreateCoordinator(BankConnectionHealth health) =>
        new(_accounts.Object, _syncService.Object, _backgroundJobs.Object, health);

    /// <summary>The sync fails with Monobank's 403 and records it through the health, as the real sync does.</summary>
    private void GivenSyncRejectsTheToken(BankConnectionHealth health) =>
        _syncService
            .Setup(s => s.PerformFullSyncAsync(_account.Id, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Returns(async () =>
            {
                await health.RecordSyncFailureAsync(
                    _account, new MonobankException("MONOBANK_TOKEN_INVALID", "Forbidden", 403), cancelledByCaller: false);
                return new SyncResult(false, 0, 0, "MONOBANK_TOKEN_INVALID", "Forbidden");
            });

    private void GivenSyncSucceeds(BankConnectionHealth health) =>
        _syncService
            .Setup(s => s.PerformFullSyncAsync(_account.Id, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Returns(async () =>
            {
                await health.RecordSyncSuccessAsync(_account);
                return new SyncResult(true, 0, 0, null, null);
            });

    private void VerifySyncCalls(Times times) =>
        _syncService.Verify(
            s => s.PerformFullSyncAsync(_account.Id, It.IsAny<CancellationToken>(), It.IsAny<string?>()), times);

    private static ConnectionHealth DormantSince(DateTimeOffset lastFailureAt) => new()
    {
        State = ConnectionHealthState.Dormant,
        ConsecutiveFailures = 300,
        FirstFailureAt = lastFailureAt.AddDays(-8),
        LastFailureAt = lastFailureAt,
        LastFailureClass = FailureClass.Transient,
    };

    private void ShouldBeLook((Job Job, IState State) scheduled, int recheck, TimeSpan delay)
    {
        scheduled.Job.Type.Should().Be(typeof(ScheduledSyncJob));
        scheduled.Job.Method.Name.Should().Be(nameof(ScheduledSyncJob.ExecuteSuspectRecheckAsync));
        scheduled.Job.Args.Should().Equal(_account.Id, recheck, _time.GetUtcNow());
        scheduled.State.Should().BeOfType<ScheduledState>().Which.EnqueueAt
            .Should().BeCloseTo(DateTime.UtcNow + delay, TimeSpan.FromSeconds(30));
    }

    // ── Dormant cadence ──────────────────────────────────────────────────────

    [Fact]
    public async Task Active_DormantConnection_ScheduledSyncWaitsADay_ThenRuns()
    {
        _credentialHealth = DormantSince(Now.AddMinutes(-30));
        var health = CreateHealth();
        GivenSyncSucceeds(health);

        var held = await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);

        held.ErrorCode.Should().Be(TransactionSyncCoordinator.DormantCode);
        VerifySyncCalls(Times.Never());

        _time.Advance(TimeSpan.FromHours(23.5));
        var ran = await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);

        ran.Success.Should().BeTrue();
        VerifySyncCalls(Times.Once());
        _credentialHealth.State.Should().Be(ConnectionHealthState.Healthy);
    }

    [Fact]
    public async Task Active_DormantAccount_IsSlowedAlone()
    {
        _account.ApplyHealth(DormantSince(Now.AddMinutes(-30)));

        (await CreateHealth().DormantSyncPausedUntilAsync(_account)).Should().Be(Now.AddMinutes(-30).AddDays(1));
    }

    [Fact]
    public async Task Shadow_DormantConnection_SyncsAsBefore()
    {
        _options.ActiveProviders.Clear();
        _credentialHealth = DormantSince(Now.AddMinutes(-30));
        var health = CreateHealth();
        GivenSyncSucceeds(health);

        var result = await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);

        result.Success.Should().BeTrue();
        VerifySyncCalls(Times.Once());
    }

    [Fact]
    public void Discovery_DormantConnection_ActiveIsSlowed_ShadowIsNot()
    {
        var connection = new BankConnectionRef(MonobankFailureClassifier.ProviderName, _credentialId, _account.UserId, "Monobank");
        var dormant = DormantSince(Now.AddMinutes(-30));

        CreateHealth().DormantPausedUntil(connection, dormant).Should().Be(Now.AddMinutes(-30).AddDays(1));

        _options.ActiveProviders.Clear();
        CreateHealth().DormantPausedUntil(connection, dormant).Should().BeNull();
    }

    [Fact]
    public void Reconnect_ClearsTheAccountsLastFailure_SoItsDormantCadenceLetsTheNextRunThrough()
    {
        _account.ApplyHealth(DormantSince(Now.AddMinutes(-30)));

        _account.MarkReconnected(Guid.NewGuid(), 10m);

        _account.Health.State.Should().Be(ConnectionHealthState.Dormant);
        _account.Health.LastFailureAt.Should().BeNull();
        ConnectionHealthPolicy.DormantPausedUntil(_account.Health, Now, _options).Should().BeNull();
    }

    // ── Suspect re-checks ────────────────────────────────────────────────────

    [Fact]
    public async Task Active_SyncThatRaisesASuspicion_LooksAgainAfterAMinute()
    {
        var health = CreateHealth();
        GivenSyncRejectsTheToken(health);

        await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);

        _credentialHealth.SuspectSince.Should().Be(Now);
        ShouldBeLook(_scheduled.Should().ContainSingle().Subject, recheck: 1, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Active_Monobank_LookNeverComesInsideItsOneCallAMinuteLimit()
    {
        _options.SuspectQuickRetryDelay = TimeSpan.FromSeconds(10);
        var health = CreateHealth();
        GivenSyncRejectsTheToken(health);

        await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);

        ShouldBeLook(_scheduled.Should().ContainSingle().Subject, recheck: 1, BankConnectionHealth.MonobankMinCallSpacing);
    }

    [Fact]
    public async Task Active_QuickLookFails_ConfirmingLookAtTheWindow_ConfirmsTheLoss()
    {
        var health = CreateHealth();
        GivenSyncRejectsTheToken(health);
        await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);
        _scheduled.Clear();

        _time.Advance(TimeSpan.FromMinutes(1));
        await CreateCoordinator(health).TriggerSuspectRecheckAsync(_account.Id, recheck: 1, scheduledAt: Now);
        _credentialHealth.State.Should().NotBe(ConnectionHealthState.ActionRequired);
        ShouldBeLook(_scheduled.Should().ContainSingle().Subject, recheck: 2, TimeSpan.FromMinutes(29));
        _scheduled.Clear();

        _time.Advance(TimeSpan.FromMinutes(29));
        await CreateCoordinator(health).TriggerSuspectRecheckAsync(_account.Id, recheck: 2, scheduledAt: Now.AddMinutes(1));

        _credentialHealth.State.Should().Be(ConnectionHealthState.ActionRequired);
        _scheduled.Should().BeEmpty();
        VerifySyncCalls(Times.Exactly(3));
    }

    [Fact]
    public async Task Active_QuickLookSucceeds_ClearsTheSuspicion_AndLooksNoMore()
    {
        var health = CreateHealth();
        GivenSyncRejectsTheToken(health);
        await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);
        _scheduled.Clear();

        _time.Advance(TimeSpan.FromMinutes(1));
        GivenSyncSucceeds(health);
        await CreateCoordinator(health).TriggerSuspectRecheckAsync(_account.Id, recheck: 1, scheduledAt: Now);

        _credentialHealth.SuspectSince.Should().BeNull();
        _credentialHealth.State.Should().Be(ConnectionHealthState.Healthy);
        _scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task LookOnASettledSuspicion_MakesNoCall()
    {
        var result = await CreateCoordinator(CreateHealth()).TriggerSuspectRecheckAsync(_account.Id, recheck: 1, scheduledAt: Now);

        result.ErrorCode.Should().Be(TransactionSyncCoordinator.RecheckNotDueCode);
        VerifySyncCalls(Times.Never());
    }

    [Fact]
    public async Task Shadow_SyncThatRaisesASuspicion_SchedulesNoLook()
    {
        _options.ActiveProviders.Clear();
        var health = CreateHealth();
        GivenSyncRejectsTheToken(health);

        await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);

        _credentialHealth.SuspectSince.Should().Be(Now);
        _scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task SiblingCardsEachScheduleALook_OnlyTheLastMakesACall()
    {
        var health = CreateHealth();
        GivenSyncRejectsTheToken(health);

        // The token's first card raised the suspicion and scheduled a look; a sibling card failed on the same token a
        // few seconds later and, reading the token before that, scheduled its own.
        await CreateCoordinator(health).TriggerScheduledSyncAsync(_account.Id);
        var firstScheduledAt = Now;
        _time.Advance(TimeSpan.FromSeconds(5));
        await health.RecordSyncFailureAsync(
            _account, new MonobankException("MONOBANK_TOKEN_INVALID", "Forbidden", 403), cancelledByCaller: false);
        var siblingScheduledAt = _time.GetUtcNow();
        _syncService.Invocations.Clear();

        _time.Advance(TimeSpan.FromSeconds(55));
        var first = await CreateCoordinator(health).TriggerSuspectRecheckAsync(_account.Id, 1, firstScheduledAt);
        first.ErrorCode.Should().Be(TransactionSyncCoordinator.RecheckNotDueCode);
        VerifySyncCalls(Times.Never());

        _time.Advance(TimeSpan.FromSeconds(5));
        await CreateCoordinator(health).TriggerSuspectRecheckAsync(_account.Id, 1, siblingScheduledAt);
        VerifySyncCalls(Times.Once());
    }
}
