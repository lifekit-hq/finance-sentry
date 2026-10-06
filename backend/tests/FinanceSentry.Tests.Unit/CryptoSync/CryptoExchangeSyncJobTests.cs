using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class CryptoExchangeSyncJobTests
{
    private readonly Mock<IExchangeCredentialRepository> _credentialRepo = new(MockBehavior.Loose);
    private readonly Mock<ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult>> _syncHandler = new(MockBehavior.Loose);
    private readonly Mock<IAlertGeneratorService> _alerts = new(MockBehavior.Loose);
    private readonly Mock<IUserAlertPreferencesReader> _userPrefs = new(MockBehavior.Loose);
    private readonly IJobFailureStreakStore _failureStreaks = new InMemoryJobFailureStreakStore();

    private sealed class InMemoryJobFailureStreakStore : IJobFailureStreakStore
    {
        private readonly Dictionary<string, JobFailureStreak> _streaks = [];

        public JobFailureStreak Get(string jobName) =>
            _streaks.TryGetValue(jobName, out var streak) ? streak : JobFailureStreak.Empty;

        public void Set(string jobName, JobFailureStreak streak) => _streaks[jobName] = streak;
    }

    private BinanceSyncJob CreateBinanceJob() =>
        new(_credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, _failureStreaks, NullLogger<BinanceSyncJob>.Instance);

    private RevolutXSyncJob CreateRevolutXJob() =>
        new(_credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, _failureStreaks, NullLogger<RevolutXSyncJob>.Instance);

    private static ExchangeCredential MakeCredential(Guid userId, string provider = CryptoExchangeProvider.Binance) =>
        ExchangeCredential.Create(userId, provider, [1], [2], [3], [4], [5], [6], 1);

    private void GivenActive(string provider, params ExchangeCredential[] credentials) =>
        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(provider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credentials);

    [Fact]
    public async Task ExecuteAsync_IteratesAllActiveCredentials()
    {
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(Guid.NewGuid()), MakeCredential(Guid.NewGuid()));

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncExchangeHoldingsResult(0, DateTime.UtcNow));

        await CreateBinanceJob().ExecuteAsync();

        _syncHandler.Verify(
            h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData(CryptoExchangeProvider.Binance)]
    [InlineData(CryptoExchangeProvider.RevolutX)]
    public async Task ExecuteAsync_EachJobSyncsOnlyItsOwnVenue(string provider)
    {
        var userId = Guid.NewGuid();
        GivenActive(provider, MakeCredential(userId, provider));

        SyncExchangeHoldingsCommand? captured = null;
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SyncExchangeHoldingsCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new SyncExchangeHoldingsResult(1, DateTime.UtcNow));

        if (provider == CryptoExchangeProvider.Binance)
            await CreateBinanceJob().ExecuteAsync();
        else
            await CreateRevolutXJob().ExecuteAsync();

        captured.Should().Be(new SyncExchangeHoldingsCommand(userId, provider));
        _credentialRepo.Verify(
            r => r.GetAllActiveUnscopedAsync(It.Is<string>(p => p != provider), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_OneCredentialFails_OtherCredentialsStillSynced_AndTheRunSucceeds()
    {
        var failingUser = Guid.NewGuid();
        var successUser = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(failingUser), MakeCredential(successUser));

        _syncHandler
            .Setup(h => h.Handle(
                It.Is<SyncExchangeHoldingsCommand>(c => c.UserId == failingUser),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated sync failure"));

        _syncHandler
            .Setup(h => h.Handle(
                It.Is<SyncExchangeHoldingsCommand>(c => c.UserId == successUser),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncExchangeHoldingsResult(2, DateTime.UtcNow));

        await CreateBinanceJob().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();

        _syncHandler.Verify(
            h => h.Handle(
                It.Is<SyncExchangeHoldingsCommand>(c => c.UserId == successUser),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EveryUserFails_FailsTheJob_SoTheConsecutiveFailureAlertCanFire()
    {
        // A swallowed total failure is a Hangfire success, and ConsecutiveFailureAlertFilter
        // (the Telegram path, #023) only ever sees failed runs.
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));

        var rejected = new RevolutXException(
            "Revolut X API error (HTTP 401): Unauthorized",
            new HttpRequestException("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized));
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(rejected);

        var thrown = await CreateRevolutXJob().Invoking(j => j.ExecuteAsync())
            .Should().ThrowAsync<AggregateException>();

        thrown.Which.InnerExceptions.Should().ContainSingle().Which.Should().BeSameAs(rejected);
        JobFailureTransientClassifier.IsTransient(thrown.Which).Should().BeFalse(
            "a rejected key is sticky and must count toward the alert streak");
    }

    [Fact]
    public async Task ExecuteAsync_VenueOutage_FailsTheJob_ButClassifiesAsTransient()
    {
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(Guid.NewGuid(), CryptoExchangeProvider.RevolutX));

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RevolutXException(
                "Revolut X API error (HTTP 503): down",
                new HttpRequestException("down", null, System.Net.HttpStatusCode.ServiceUnavailable)));

        var thrown = await CreateRevolutXJob().Invoking(j => j.ExecuteAsync())
            .Should().ThrowAsync<AggregateException>();

        JobFailureTransientClassifier.IsTransient(thrown.Which).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_NoConnectedUsers_Succeeds()
    {
        GivenActive(CryptoExchangeProvider.RevolutX);

        await CreateRevolutXJob().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_Failure_RaisesTheInAppSyncFailureAlertUnderTheVenueSlug()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));
        _userPrefs
            .Setup(p => p.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, SyncFailureAlerts: true));
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RevolutXException("boom"));

        await CreateRevolutXJob().Invoking(j => j.ExecuteAsync()).Should().ThrowAsync<AggregateException>();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, CryptoExchangeProvider.RevolutX, null, null, nameof(RevolutXException),
                SyncFailureClass.Credential, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void GivenAlertsOn(Guid userId) =>
        _userPrefs
            .Setup(p => p.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, SyncFailureAlerts: true));

    private void GivenSyncFails(Exception ex) =>
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

    private void VerifyAlerts(Guid userId, string provider, SyncFailureClass failureClass, Times times) =>
        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, provider, null, null, It.IsAny<string?>(), failureClass, It.IsAny<CancellationToken>()),
            times);

    private void VerifyAnyAlerts(Guid userId, string provider, Times times) =>
        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, provider, null, null, It.IsAny<string?>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()),
            times);

    // The streak gate: a transient failure alerts only on the 3rd consecutive tick; an all-users failure
    // throws an AggregateException every tick, which the job-failure filter handles separately.
    private static async Task RunTicks(Func<Task> tick, int count)
    {
        for (var i = 0; i < count; i++)
        {
            try { await tick(); }
            catch (AggregateException) { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_BinanceHttp500_IsTransient_AlertsOnlyAtThirdConsecutiveTick_WithOutageClass()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        GivenSyncFails(new BinanceException("Binance API error: HTTP 500", venueStatusCode: 500));

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 2);
        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Never());

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 1);
        VerifyAlerts(userId, CryptoExchangeProvider.Binance, SyncFailureClass.Outage, Times.Once());

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 2);
        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_RevolutXUnreachable_IsTransient_NoAlertOnFirstTick()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));
        GivenAlertsOn(userId);
        GivenSyncFails(new RevolutXException(
            "Revolut X is unreachable.", new HttpRequestException("Connection refused", new System.Net.Sockets.SocketException())));

        await RunTicks(() => CreateRevolutXJob().ExecuteAsync(), 1);

        VerifyAnyAlerts(userId, CryptoExchangeProvider.RevolutX, Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_RevolutXTimeout_IsTransient_NoAlertOnFirstTick()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));
        GivenAlertsOn(userId);
        GivenSyncFails(new RevolutXException("Revolut X did not respond in time.", new TaskCanceledException("timeout")));

        await RunTicks(() => CreateRevolutXJob().ExecuteAsync(), 1);

        VerifyAnyAlerts(userId, CryptoExchangeProvider.RevolutX, Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_BinanceRejectedKey_AlertsImmediatelyWithReconnectClass()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        GivenSyncFails(new BinanceException("Invalid API-key, IP, or permissions for action.", binanceErrorCode: -2015, venueStatusCode: 401));

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 1);

        VerifyAlerts(userId, CryptoExchangeProvider.Binance, SyncFailureClass.Credential, Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_SuccessBetweenTransientFailures_ResetsTheStreak()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        _syncHandler
            .SetupSequence(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BinanceException("HTTP 500", venueStatusCode: 500))
            .ThrowsAsync(new BinanceException("HTTP 500", venueStatusCode: 500))
            .ReturnsAsync(new SyncExchangeHoldingsResult(1, DateTime.UtcNow))
            .ThrowsAsync(new BinanceException("HTTP 500", venueStatusCode: 500))
            .ThrowsAsync(new BinanceException("HTTP 500", venueStatusCode: 500));

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 5);

        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Never());
    }
}
