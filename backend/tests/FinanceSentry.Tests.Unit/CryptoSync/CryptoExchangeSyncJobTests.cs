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

    private BinanceSyncJob CreateBinanceJob() =>
        new(_credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, NullLogger<BinanceSyncJob>.Instance);

    private RevolutXSyncJob CreateRevolutXJob() =>
        new(_credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, NullLogger<RevolutXSyncJob>.Instance);

    private static ExchangeCredential MakeCredential(Guid userId, string provider = CryptoExchangeProvider.Binance) =>
        ExchangeCredential.Create(userId, provider, [1], [2], [3], [4], [5], [6], 1);

    private void GivenActive(string provider, params ExchangeCredential[] credentials) =>
        _credentialRepo
            .Setup(r => r.GetAllActiveAsync(provider, It.IsAny<CancellationToken>()))
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
            r => r.GetAllActiveAsync(It.Is<string>(p => p != provider), It.IsAny<CancellationToken>()),
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
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
