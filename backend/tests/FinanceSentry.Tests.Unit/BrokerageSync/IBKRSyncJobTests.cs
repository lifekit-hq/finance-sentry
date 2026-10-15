using System.Net;
using System.Security.Cryptography;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.BrokerageSync.Application.Commands;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync;

public class IBKRSyncJobTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);

    private readonly Mock<IIBKRCredentialRepository> _credentialRepo = new(MockBehavior.Loose);
    private readonly Mock<ICommandHandler<SyncIBKRHoldingsCommand, SyncIBKRHoldingsResult>> _syncHandler = new(MockBehavior.Loose);
    private readonly Mock<IAlertGeneratorService> _alerts = new(MockBehavior.Loose);
    private readonly Mock<IUserAlertPreferencesReader> _userPrefs = new(MockBehavior.Loose);
    private readonly IJobFailureStreakStore _failureStreaks = new InMemoryJobFailureStreakStore();
    private readonly Mock<IConnectionHealthShadow> _connectionHealth = new(MockBehavior.Loose);
    private readonly FakeTimeProvider _time = new(Start);

    // IBKR is live in the committed appsettings; the shadow-mode tests clear it.
    private readonly ConnectionHealthOptions _options = HealthOptions.With("ibkr");

    public IBKRSyncJobTests()
    {
        _userPrefs
            .Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(true, 0m, true));
    }

    private IBKRSyncJob CreateJob(IConnectionHealthShadow? shadow = null) =>
        new(_credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, _failureStreaks,
            shadow ?? _connectionHealth.Object, new IbkrFailureClassifier(), HealthOptions.Monitor(_options), _time,
            NullLogger<IBKRSyncJob>.Instance);

    /// <summary>The job with the real policy behind it, on the test's clock.</summary>
    private IBKRSyncJob CreatePolicyJob() => CreateJob(ShadowRecorders.Create(_time, _options));

    /// <summary>Runs <paramref name="count"/> 15-minute ticks with the real policy, the first one now.</summary>
    private async Task RunTicksAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0) _time.Advance(Tick);
            await CreatePolicyJob().ExecuteAsync();
        }
    }

    private void GivenShadowMode() => _options.ActiveProviders.Clear();

    private static IBKRCredential MakeCredential(Guid userId)
    {
        var c = new IBKRCredential(
            userId, "FINSENTRY", "access-token", "dh-pem",
            [1], [2], [3], [4], [5], [6], [7], [8], [9], keyVersion: 1);
        c.UpdateAccountId("U1234567");
        return c;
    }

    private IBKRCredential GivenOneCredential(Guid userId)
    {
        var credential = MakeCredential(userId);
        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([credential]);
        return credential;
    }

    private void GivenSyncFails(Exception ex) =>
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

    private void GivenSyncSucceeds() =>
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncIBKRHoldingsResult(1, DateTime.UtcNow));

    private void VerifyAlerts(Guid userId, SyncFailureClass failureClass, Times times) =>
        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, "ibkr", null, null, It.IsAny<string?>(), failureClass, It.IsAny<CancellationToken>()),
            times);

    private void VerifyEscalated(Guid userId, Times times) =>
        _alerts.Verify(
            a => a.EscalateSyncFailureAlertAsync(userId, "ibkr", null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            times);

    private void VerifyNoAlert()
    {
        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _alerts.Verify(
            a => a.EscalateSyncFailureAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static BrokerAuthException Ibkr503() =>
        new("IBKR live-session-token request failed (503): Error 500 - Server Error", "IBKR", HttpStatusCode.ServiceUnavailable);

    private static BrokerAuthException Ibkr401() =>
        new("IBKR rejected the signed request (401).", "IBKR", HttpStatusCode.Unauthorized);

    private static BrokerAuthException IbkrBackendDown() =>
        new("IBKR live-session-token request failed (401): {\"error\":\"backend down\",\"statusCode\":401}",
            "IBKR", HttpStatusCode.Unauthorized);

    // ── Every mode ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_IteratesAllActiveCredentials()
    {
        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(Guid.NewGuid()), MakeCredential(Guid.NewGuid())]);
        GivenSyncSucceeds();

        await CreateJob().ExecuteAsync();

        _syncHandler.Verify(
            h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_DispatchesSyncCommandPerCredential()
    {
        var userId = Guid.NewGuid();
        GivenOneCredential(userId);
        SyncIBKRHoldingsCommand? capturedCommand = null;
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SyncIBKRHoldingsCommand, CancellationToken>((cmd, _) => capturedCommand = cmd)
            .ReturnsAsync(new SyncIBKRHoldingsResult(1, DateTime.UtcNow));

        await CreateJob().ExecuteAsync();

        capturedCommand.Should().NotBeNull();
        capturedCommand!.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task ExecuteAsync_OneCredentialFails_OtherCredentialsStillSynced()
    {
        var failingUser = Guid.NewGuid();
        var successUser = Guid.NewGuid();
        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(failingUser), MakeCredential(successUser)]);
        _syncHandler
            .Setup(h => h.Handle(It.Is<SyncIBKRHoldingsCommand>(c => c.UserId == failingUser), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated sync failure"));
        _syncHandler
            .Setup(h => h.Handle(It.Is<SyncIBKRHoldingsCommand>(c => c.UserId == successUser), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncIBKRHoldingsResult(2, DateTime.UtcNow));

        await CreateJob().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();

        _syncHandler.Verify(
            h => h.Handle(It.Is<SyncIBKRHoldingsCommand>(c => c.UserId == successUser), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public static TheoryData<Exception, FailureClass, FailureStrength> RecordedFailureCases => new()
    {
        { Ibkr401(), FailureClass.Credential, FailureStrength.Suspect },
        { Ibkr503(), FailureClass.Transient, FailureStrength.Definitive },
        { new BrokerCredentialUnreadableException("IBKR", new CryptographicException("tag mismatch")), FailureClass.Credential, FailureStrength.Definitive },
        { new InvalidOperationException("boom"), FailureClass.Unknown, FailureStrength.Definitive },
    };

    [Theory]
    [MemberData(nameof(RecordedFailureCases))]
    public async Task Failure_RecordsTheClassifiersReadingWithThePolicy_InEitherMode(
        Exception ex, FailureClass expectedClass, FailureStrength expectedStrength)
    {
        foreach (var shadow in new[] { false, true })
        {
            if (shadow) GivenShadowMode();
            _connectionHealth.Invocations.Clear();
            var credential = GivenOneCredential(Guid.NewGuid());
            GivenSyncFails(ex);

            await CreateJob().ExecuteAsync();

            _connectionHealth.Verify(s => s.RecordFailureAsync(
                It.Is<ConnectionHealthSubject>(subject =>
                    subject.Id == credential.Id && subject.Provider == "ibkr" && subject.Kind == nameof(IBKRCredential)),
                It.IsAny<ConnectionHealth>(),
                It.Is<ProviderFailure>(f => f.Class == expectedClass && f.Strength == expectedStrength),
                It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task Success_RecordsSuccessWithThePolicy()
    {
        var credential = GivenOneCredential(Guid.NewGuid());
        GivenSyncSucceeds();

        await CreateJob().ExecuteAsync();

        _connectionHealth.Verify(s => s.RecordSuccessAsync(
            It.Is<ConnectionHealthSubject>(subject => subject.Id == credential.Id),
            It.IsAny<ConnectionHealth>(),
            It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SwitchingIbkrInActiveProviders_FlipsWhoRaisesTheAlert()
    {
        var userId = Guid.NewGuid();
        GivenOneCredential(userId);
        GivenSyncFails(Ibkr401());

        // Active: a bare 401 is only suspect, so the policy waits for its confirmation.
        await CreatePolicyJob().ExecuteAsync();
        VerifyNoAlert();

        // Shadow: the job's own streak raises the reconnect alert at once, as before the policy.
        GivenShadowMode();
        await CreatePolicyJob().ExecuteAsync();
        VerifyAlerts(userId, SyncFailureClass.Credential, Times.Once());
    }

    // ── Active: the policy drives the alert (Option B, S3) ───────────────────

    [Fact]
    public async Task Active_TransientFailure_NoAlertBeforeThreeFailuresAndTwoHours_ThenOneOutageAlert()
    {
        var userId = Guid.NewGuid();
        GivenOneCredential(userId);
        GivenSyncFails(Ibkr503());

        // Three failures in 30 minutes: the shadow streak alerts here, the policy waits for 2 h.
        await RunTicksAsync(3);
        VerifyNoAlert();

        // On to 1 h 45 min (8 failures): still Degraded.
        _time.Advance(Tick);
        await RunTicksAsync(5);
        VerifyNoAlert();

        // The tick 2 h after the first failure makes it Failing.
        _time.Advance(Tick);
        await CreatePolicyJob().ExecuteAsync();
        VerifyAlerts(userId, SyncFailureClass.Outage, Times.Once());

        // Failing holds; the alert is not raised again.
        _time.Advance(Tick);
        await RunTicksAsync(4);
        VerifyAlerts(userId, SyncFailureClass.Outage, Times.Once());
    }

    [Fact]
    public async Task Active_BackendDown401_IsAnOutage_NeverAReconnect()
    {
        var userId = Guid.NewGuid();
        GivenOneCredential(userId);
        GivenSyncFails(IbkrBackendDown());

        await RunTicksAsync(9);

        VerifyAlerts(userId, SyncFailureClass.Outage, Times.Once());
        VerifyEscalated(userId, Times.Never());
    }

    [Fact]
    public async Task Active_Bare401_IsConfirmedOnTheTickThirtyMinutesLater_ThenEscalatesToTheReconnectAlert()
    {
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        GivenSyncFails(Ibkr401());

        await RunTicksAsync(2);
        VerifyNoAlert();
        credential.Health.State.Should().Be(ConnectionHealthState.Degraded);

        _time.Advance(Tick);
        await CreatePolicyJob().ExecuteAsync();

        VerifyEscalated(userId, Times.Once());
        credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);

        // ActionRequired holds until a success; later failures raise nothing more.
        _time.Advance(Tick);
        await RunTicksAsync(3);
        VerifyEscalated(userId, Times.Once());
    }

    [Fact]
    public async Task Active_UnreadableStoredCredential_EscalatesAtOnce()
    {
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        GivenSyncFails(new BrokerCredentialUnreadableException("IBKR", new CryptographicException("tag mismatch")));

        await RunTicksAsync(1);

        VerifyEscalated(userId, Times.Once());
        credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
    }

    [Fact]
    public async Task Active_TransientThenRevoked_TheOutageAlertGivesWayToTheReconnectAlert()
    {
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        GivenSyncFails(Ibkr503());
        await RunTicksAsync(9);
        VerifyAlerts(userId, SyncFailureClass.Outage, Times.Once());

        // The key is revoked while the outage alert is open: IBKR now answers 401, confirmed 30 min later.
        GivenSyncFails(Ibkr401());
        _time.Advance(Tick);
        await RunTicksAsync(3);

        VerifyEscalated(userId, Times.Once());
        credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
    }

    [Fact]
    public async Task Active_UnexpectedException_CountsAsUnknown_AndAlertsOnceItPersists()
    {
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        GivenSyncFails(new InvalidOperationException("Unexpected holdings payload."));

        await RunTicksAsync(8);
        VerifyNoAlert();
        credential.Health.ConsecutiveFailures.Should().Be(8);

        _time.Advance(Tick);
        await CreatePolicyJob().ExecuteAsync();
        VerifyAlerts(userId, SyncFailureClass.Unknown, Times.Once());
    }

    [Fact]
    public async Task Active_SuccessAfterTheAlert_ResolvesIt()
    {
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        GivenSyncFails(Ibkr503());
        await RunTicksAsync(9);
        VerifyAlerts(userId, SyncFailureClass.Outage, Times.Once());

        GivenSyncSucceeds();
        _time.Advance(Tick);
        await RunTicksAsync(2);

        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(userId, "ibkr", null, It.IsAny<CancellationToken>()), Times.Once);
        credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
    }

    [Fact]
    public async Task Active_SuccessOnAHealthyConnection_ResolvesNothing()
    {
        GivenOneCredential(Guid.NewGuid());
        GivenSyncSucceeds();

        await RunTicksAsync(2);

        _alerts.Verify(
            a => a.ResolveSyncFailureAlertAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Shadow: the job's own streak, exactly as before the policy ───────────

    [Fact]
    public async Task Shadow_SingleTransientFailure_DoesNotAlert()
    {
        GivenShadowMode();
        GivenOneCredential(Guid.NewGuid());
        GivenSyncFails(Ibkr503());

        await CreatePolicyJob().ExecuteAsync();

        VerifyNoAlert();
    }

    [Fact]
    public async Task Shadow_TransientFailurePersistsAcrossTicks_EscalatesToOneOutageAlert_WhileThePolicyOnlyRecords()
    {
        GivenShadowMode();
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        GivenSyncFails(Ibkr503());

        await RunTicksAsync(5);

        VerifyAlerts(userId, SyncFailureClass.Outage, Times.Once());
        // The policy would still hold it Degraded: 2 h without success have not passed.
        credential.Health.State.Should().Be(ConnectionHealthState.Degraded);
        credential.Health.ConsecutiveFailures.Should().Be(5);
    }

    [Fact]
    public async Task Shadow_BackendDownPersists_EscalatesWithOutageCopyNotReconnect()
    {
        GivenShadowMode();
        var userId = Guid.NewGuid();
        GivenOneCredential(userId);
        GivenSyncFails(IbkrBackendDown());

        await RunTicksAsync(3);

        VerifyAlerts(userId, SyncFailureClass.Outage, Times.Once());
    }

    [Fact]
    public async Task Shadow_Bare401_AlertsImmediatelyWithTheReconnectClass_WhileThePolicyOnlySuspects()
    {
        GivenShadowMode();
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        GivenSyncFails(Ibkr401());

        await CreatePolicyJob().ExecuteAsync();

        VerifyAlerts(userId, SyncFailureClass.Credential, Times.Once());
        VerifyEscalated(userId, Times.Never());
        credential.Health.State.Should().Be(ConnectionHealthState.Degraded);
        credential.Health.SuspectSince.Should().NotBeNull();
        _credentialRepo.Verify(r => r.SaveHealthUnscopedAsync(credential.Id, It.IsAny<ConnectionHealth>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Shadow_NonCredentialFailure_AlertsImmediatelyWithUnknownClassNotReconnect()
    {
        GivenShadowMode();
        var userId = Guid.NewGuid();
        GivenOneCredential(userId);
        GivenSyncFails(new InvalidOperationException("Unexpected holdings payload."));

        await CreatePolicyJob().ExecuteAsync();

        VerifyAlerts(userId, SyncFailureClass.Unknown, Times.Once());
    }

    [Fact]
    public async Task Shadow_SuccessAfterFailure_ResetsStreakAndResolvesAlert()
    {
        GivenShadowMode();
        var userId = Guid.NewGuid();
        GivenOneCredential(userId);
        _syncHandler
            .SetupSequence(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Ibkr503())
            .ReturnsAsync(new SyncIBKRHoldingsResult(1, DateTime.UtcNow));

        await RunTicksAsync(2);

        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(userId, "ibkr", null, It.IsAny<CancellationToken>()), Times.Once);
        _failureStreaks.Get("ibkr-sync:" + userId).Should().Be(JobFailureStreak.Empty);
    }

    // ── A confirmed-lost credential is not re-hit every tick ─────────────────

    private static ConnectionHealth ActionRequiredSince(DateTimeOffset lastFailureAt) =>
        new()
        {
            State = ConnectionHealthState.ActionRequired,
            ConsecutiveFailures = 3,
            FirstFailureAt = lastFailureAt.AddHours(-1),
            LastFailureAt = lastFailureAt,
            LastFailureClass = FailureClass.Credential,
            LastFailureCode = "HTTP_401",
        };

    private void VerifySyncCalls(Times times) =>
        _syncHandler.Verify(
            h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()), times);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActionRequired_ScheduledRun_SkipsTheCredential_InBothModes(bool policyDrivesAlerts)
    {
        if (!policyDrivesAlerts) GivenShadowMode();
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        credential.ApplyHealth(ActionRequiredSince(Start.AddHours(-1)));
        GivenSyncFails(Ibkr401());

        await RunTicksAsync(8);

        VerifySyncCalls(Times.Never());
        VerifyNoAlert();
        _credentialRepo.Verify(
            r => r.SaveHealthUnscopedAsync(It.IsAny<Guid>(), It.IsAny<ConnectionHealth>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ActionRequired_SkipsOnlyTheLostCredential_NotTheOthers()
    {
        var lost = MakeCredential(Guid.NewGuid());
        lost.ApplyHealth(ActionRequiredSince(Start.AddHours(-1)));
        var healthy = MakeCredential(Guid.NewGuid());
        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([lost, healthy]);
        GivenSyncSucceeds();

        await CreatePolicyJob().ExecuteAsync();

        _syncHandler.Verify(
            h => h.Handle(It.Is<SyncIBKRHoldingsCommand>(c => c.UserId == lost.UserId), It.IsAny<CancellationToken>()),
            Times.Never);
        _syncHandler.Verify(
            h => h.Handle(It.Is<SyncIBKRHoldingsCommand>(c => c.UserId == healthy.UserId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ActionRequired_ProbesOncePerDay_TimedFromTheLastFailure()
    {
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        credential.ApplyHealth(ActionRequiredSince(Start.AddDays(-1)));
        GivenSyncFails(Ibkr401());

        // The day has passed since the last failure: the first tick probes, and the failed probe renews the pause.
        await CreatePolicyJob().ExecuteAsync();
        VerifySyncCalls(Times.Once());
        credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        credential.Health.LastFailureAt.Should().Be(Start);

        // 95 more 15-minute ticks, all inside the day.
        for (var i = 0; i < 95; i++)
        {
            _time.Advance(Tick);
            await CreatePolicyJob().ExecuteAsync();
        }
        VerifySyncCalls(Times.Once());

        // The tick a day after the probe is the next one.
        _time.Advance(Tick);
        await CreatePolicyJob().ExecuteAsync();
        VerifySyncCalls(Times.Exactly(2));
        VerifyEscalated(userId, Times.Never());
    }

    [Fact]
    public async Task ActionRequired_SuccessfulProbe_ClearsItThroughThePolicy_AndResolvesTheReconnectAlert()
    {
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        credential.ApplyHealth(ActionRequiredSince(Start.AddDays(-1)));
        GivenSyncSucceeds();

        await CreatePolicyJob().ExecuteAsync();

        credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
        credential.Health.LastSuccessAt.Should().Be(Start);
        _credentialRepo.Verify(
            r => r.SaveHealthUnscopedAsync(credential.Id, It.Is<ConnectionHealth>(h => h.State == ConnectionHealthState.Healthy), It.IsAny<CancellationToken>()),
            Times.Once);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(userId, "ibkr", null, It.IsAny<CancellationToken>()), Times.Once);

        // Healthy again: the next tick syncs as usual.
        _time.Advance(Tick);
        await CreatePolicyJob().ExecuteAsync();
        VerifySyncCalls(Times.Exactly(2));
    }

    [Fact]
    public async Task ActionRequired_AfterReactivate_TheNextScheduledRunSyncs()
    {
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        credential.ApplyHealth(ActionRequiredSince(Start.AddHours(-1)));
        credential.Reactivate(
            "FINSENTRY", "access-token", "dh-pem", [1], [2], [3], [4], [5], [6], [7], [8], [9], keyVersion: 1);
        GivenSyncSucceeds();
        credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);

        await CreatePolicyJob().ExecuteAsync();

        VerifySyncCalls(Times.Once());
        credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
        credential.Health.LastSuccessAt.Should().Be(Start);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(userId, "ibkr", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Dormant cadence (S4) ──────────────────────────────────────────

    private static ConnectionHealth DormantSince(DateTimeOffset lastFailureAt) => new()
    {
        State = ConnectionHealthState.Dormant,
        ConsecutiveFailures = 700,
        FirstFailureAt = lastFailureAt.AddDays(-8),
        LastFailureAt = lastFailureAt,
        LastFailureClass = FailureClass.Transient,
        StateChangedAt = lastFailureAt.AddDays(-1),
    };

    [Fact]
    public async Task Active_Dormant_SyncsOnceADay_TimedFromTheLastFailure()
    {
        var credential = GivenOneCredential(Guid.NewGuid());
        credential.ApplyHealth(DormantSince(Start.AddHours(-1)));
        GivenSyncFails(Ibkr503());

        // 15-minute ticks for the rest of the day after the last failure: none of them calls IBKR.
        for (var i = 0; i < 90; i++)
        {
            if (i > 0) _time.Advance(Tick);
            await CreatePolicyJob().ExecuteAsync();
        }
        VerifySyncCalls(Times.Never());

        // A day after it, the run tries it; the failure renews the pause and raises nothing new.
        _time.Advance(Tick * 3);
        await CreatePolicyJob().ExecuteAsync();
        VerifySyncCalls(Times.Once());
        credential.Health.State.Should().Be(ConnectionHealthState.Dormant);
        VerifyNoAlert();

        _time.Advance(Tick);
        await CreatePolicyJob().ExecuteAsync();
        VerifySyncCalls(Times.Once());
    }

    [Fact]
    public async Task Active_Dormant_SuccessGoesHealthy_ResolvesTheAlert_AndTheCadenceIsBack()
    {
        var userId = Guid.NewGuid();
        var credential = GivenOneCredential(userId);
        credential.ApplyHealth(DormantSince(Start.AddDays(-1)));
        GivenSyncSucceeds();

        await CreatePolicyJob().ExecuteAsync();

        credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(userId, "ibkr", null, It.IsAny<CancellationToken>()), Times.Once);

        _time.Advance(Tick);
        await CreatePolicyJob().ExecuteAsync();
        VerifySyncCalls(Times.Exactly(2));
    }

    [Fact]
    public async Task Active_Dormant_AfterReactivate_TheNextScheduledRunSyncs()
    {
        var credential = GivenOneCredential(Guid.NewGuid());
        credential.ApplyHealth(DormantSince(Start.AddMinutes(-15)));
        credential.Reactivate(
            "FINSENTRY", "access-token", "dh-pem", [1], [2], [3], [4], [5], [6], [7], [8], [9], keyVersion: 1);
        GivenSyncSucceeds();

        await CreatePolicyJob().ExecuteAsync();

        VerifySyncCalls(Times.Once());
        credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
    }

    [Fact]
    public async Task Shadow_Dormant_SyncsEveryTick_AsBefore()
    {
        GivenShadowMode();
        var credential = GivenOneCredential(Guid.NewGuid());
        credential.ApplyHealth(DormantSince(Start.AddMinutes(-15)));
        GivenSyncFails(Ibkr503());

        await RunTicksAsync(4);

        VerifySyncCalls(Times.Exactly(4));
    }
}
