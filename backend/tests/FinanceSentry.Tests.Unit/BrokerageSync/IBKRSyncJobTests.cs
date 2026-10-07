using System.Net;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.BrokerageSync.Application.Commands;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync;

public class IBKRSyncJobTests
{
    private readonly Mock<IIBKRCredentialRepository> _credentialRepo = new(MockBehavior.Loose);
    private readonly Mock<ICommandHandler<SyncIBKRHoldingsCommand, SyncIBKRHoldingsResult>> _syncHandler = new(MockBehavior.Loose);

    private readonly Mock<FinanceSentry.Core.Interfaces.IAlertGeneratorService> _alerts = new(MockBehavior.Loose);
    private readonly Mock<FinanceSentry.Core.Interfaces.IUserAlertPreferencesReader> _userPrefs = new(MockBehavior.Loose);
    private readonly IJobFailureStreakStore _failureStreaks = new InMemoryJobFailureStreakStore();
    private readonly Mock<IConnectionHealthShadow> _connectionHealth = new(MockBehavior.Loose);

    public IBKRSyncJobTests()
    {
        _userPrefs
            .Setup(p => p.GetAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new FinanceSentry.Core.Interfaces.UserAlertPreferences(true, 0m, true));
    }

    private IBKRSyncJob CreateJob() =>
        new(_credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, _failureStreaks, _connectionHealth.Object, NullLogger<IBKRSyncJob>.Instance);

    private sealed class InMemoryJobFailureStreakStore : IJobFailureStreakStore
    {
        private readonly Dictionary<string, JobFailureStreak> _streaks = [];

        public JobFailureStreak Get(string jobName) =>
            _streaks.TryGetValue(jobName, out var streak) ? streak : JobFailureStreak.Empty;

        public void Set(string jobName, JobFailureStreak streak) => _streaks[jobName] = streak;
    }

    private static IBKRCredential MakeCredential(Guid userId)
    {
        var c = new IBKRCredential(
            userId, "FINSENTRY", "access-token", "dh-pem",
            [1], [2], [3], [4], [5], [6], [7], [8], [9], keyVersion: 1);
        c.UpdateAccountId("U1234567");
        return c;
    }

    [Fact]
    public async Task ExecuteAsync_IteratesAllActiveCredentials()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(user1), MakeCredential(user2)]);

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncIBKRHoldingsResult(0, DateTime.UtcNow));

        await CreateJob().ExecuteAsync();

        _syncHandler.Verify(
            h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_DispatchesSyncCommandPerCredential()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        SyncIBKRHoldingsCommand? capturedCommand = null;
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SyncIBKRHoldingsCommand, CancellationToken>((cmd, _) =>
                capturedCommand = cmd)
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
            .Setup(h => h.Handle(
                It.Is<SyncIBKRHoldingsCommand>(c => c.UserId == failingUser),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated sync failure"));

        _syncHandler
            .Setup(h => h.Handle(
                It.Is<SyncIBKRHoldingsCommand>(c => c.UserId == successUser),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncIBKRHoldingsResult(2, DateTime.UtcNow));

        await CreateJob().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();

        _syncHandler.Verify(
            h => h.Handle(
                It.Is<SyncIBKRHoldingsCommand>(c => c.UserId == successUser),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SingleTransientFailure_DoesNotAlert()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerAuthException(
                "IBKR live-session-token request failed (503): Error 500 - Server Error", "IBKR", HttpStatusCode.ServiceUnavailable));

        await CreateJob().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, "ibkr", null, null, It.IsAny<string>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailurePersistsAcrossTicks_EscalatesToOneAlert()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerAuthException(
                "IBKR live-session-token request failed (503): Error 500 - Server Error", "IBKR", HttpStatusCode.ServiceUnavailable));

        // Simulate three consecutive 15-minute ticks all hitting the same transient blip.
        await CreateJob().ExecuteAsync();
        await CreateJob().ExecuteAsync();
        await CreateJob().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, "ibkr", null, null, It.IsAny<string>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Http401WithBackendDownBody_IsTransient_NoAlertOnFirstTick()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerAuthException(
                "IBKR live-session-token request failed (401): {\"error\":\"backend down\",\"statusCode\":401}",
                "IBKR", HttpStatusCode.Unauthorized));

        await CreateJob().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_BackendDownPersists_EscalatesWithOutageCopyNotReconnect()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerAuthException(
                "IBKR live-session-token request failed (401): {\"error\":\"backend down\",\"statusCode\":401}",
                "IBKR", HttpStatusCode.Unauthorized));

        await CreateJob().ExecuteAsync();
        await CreateJob().ExecuteAsync();
        await CreateJob().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, "ibkr", null, null, It.IsAny<string>(), SyncFailureClass.Outage, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NonTransientFailure_AlertsImmediately()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerAuthException(
                "IBKR rejected the signed request (401).", "IBKR", HttpStatusCode.Unauthorized));

        await CreateJob().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, "ibkr", null, null, It.IsAny<string>(), SyncFailureClass.Credential, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NonCredentialFailure_AlertsImmediatelyWithUnknownClassNotReconnect()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected holdings payload."));

        await CreateJob().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, "ibkr", null, null, It.IsAny<string>(), SyncFailureClass.Unknown, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessAfterFailure_ResetsStreakAndResolvesAlert()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        _syncHandler
            .SetupSequence(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerAuthException(
                "IBKR live-session-token request failed (503): Error 500 - Server Error", "IBKR", HttpStatusCode.ServiceUnavailable))
            .ReturnsAsync(new SyncIBKRHoldingsResult(1, DateTime.UtcNow));

        await CreateJob().ExecuteAsync();
        await CreateJob().ExecuteAsync();

        _alerts.Verify(
            a => a.ResolveSyncFailureAlertAsync(userId, "ibkr", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Connection health, shadow mode (Option B, S1) ───────────────────────

    public static TheoryData<Exception, FailureClass, FailureStrength> ShadowFailureCases => new()
    {
        // A bare IBKR 401 is only suspect: IBKR sends it for reasons other than a dead credential (report §5).
        { new BrokerAuthException("IBKR rejected the token (401)", "IBKR", HttpStatusCode.Unauthorized), FailureClass.Credential, FailureStrength.Suspect },
        { new BrokerAuthException("IBKR (503): Error 500 - Server Error", "IBKR", HttpStatusCode.ServiceUnavailable), FailureClass.Transient, FailureStrength.Definitive },
        { new InvalidOperationException("boom"), FailureClass.Unknown, FailureStrength.Definitive },
    };

    [Theory]
    [MemberData(nameof(ShadowFailureCases))]
    public async Task ExecuteAsync_Failure_RecordsTheJobsOwnClassificationWithTheShadow(
        Exception ex, FailureClass expectedClass, FailureStrength expectedStrength)
    {
        var credential = MakeCredential(Guid.NewGuid());
        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([credential]);
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

        await CreateJob().ExecuteAsync();

        _connectionHealth.Verify(s => s.RecordFailureAsync(
            It.Is<ConnectionHealthSubject>(subject =>
                subject.Id == credential.Id && subject.Provider == "ibkr" && subject.Kind == nameof(IBKRCredential)),
            It.IsAny<ConnectionHealth>(),
            It.Is<ProviderFailure>(f => f.Class == expectedClass && f.Strength == expectedStrength),
            It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Success_RecordsSuccessWithTheShadow()
    {
        var credential = MakeCredential(Guid.NewGuid());
        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([credential]);
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncIBKRHoldingsResult(0, DateTime.UtcNow));

        await CreateJob().ExecuteAsync();

        _connectionHealth.Verify(s => s.RecordSuccessAsync(
            It.Is<ConnectionHealthSubject>(subject => subject.Id == credential.Id),
            It.IsAny<ConnectionHealth>(),
            It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithTheRealShadow_CredentialAlertFiresAsBefore_WhileThePolicyOnlySuspects()
    {
        var userId = Guid.NewGuid();
        var credential = MakeCredential(userId);
        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([credential]);
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerAuthException("IBKR rejected the token (401)", "IBKR", HttpStatusCode.Unauthorized));
        var job = new IBKRSyncJob(
            _credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, _failureStreaks,
            ShadowRecorders.Create(), NullLogger<IBKRSyncJob>.Instance);

        await job.ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, "ibkr", null, null, It.IsAny<string>(), SyncFailureClass.Credential, It.IsAny<CancellationToken>()),
            Times.Once);
        credential.Health.State.Should().Be(ConnectionHealthState.Degraded);
        credential.Health.SuspectSince.Should().NotBeNull();
        _credentialRepo.Verify(r => r.SaveHealthUnscopedAsync(credential.Id, It.IsAny<ConnectionHealth>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
