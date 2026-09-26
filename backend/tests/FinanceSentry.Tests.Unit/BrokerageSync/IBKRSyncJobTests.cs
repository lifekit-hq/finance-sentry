using System.Net;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.BrokerageSync.Application.Commands;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;
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

    public IBKRSyncJobTests()
    {
        _userPrefs
            .Setup(p => p.GetAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new FinanceSentry.Core.Interfaces.UserAlertPreferences(true, 0m, true));
    }

    private IBKRSyncJob CreateJob() =>
        new(_credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, _failureStreaks, NullLogger<IBKRSyncJob>.Instance);

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
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerAuthException(
                "IBKR live-session-token request failed (503): Error 500 - Server Error", "IBKR", HttpStatusCode.ServiceUnavailable));

        await CreateJob().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, "ibkr", null, null, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailurePersistsAcrossTicks_EscalatesToOneAlert()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
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
                userId, "ibkr", null, null, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NonTransientFailure_AlertsImmediately()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCredential(userId)]);

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncIBKRHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BrokerAuthException(
                "IBKR rejected the signed request (401).", "IBKR", HttpStatusCode.Unauthorized));

        await CreateJob().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, "ibkr", null, null, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessAfterFailure_ResetsStreakAndResolvesAlert()
    {
        var userId = Guid.NewGuid();

        _credentialRepo
            .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
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
}
