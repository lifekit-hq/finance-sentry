using FinanceSentry.Core.Cqrs;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class ConnectExchangeCommandTests
{
    private readonly Mock<IExchangeCredentialRepository> _credentialRepo = new(MockBehavior.Strict);
    private readonly Mock<ICryptoExchangeAdapter> _binance = new(MockBehavior.Strict);
    private readonly Mock<ICryptoExchangeAdapter> _revolutX = new(MockBehavior.Strict);
    private readonly Mock<ICredentialEncryptionService> _encryption = new(MockBehavior.Strict);
    private readonly Mock<ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult>> _syncHandler = new(MockBehavior.Strict);

    public ConnectExchangeCommandTests()
    {
        _binance.SetupGet(a => a.ExchangeName).Returns(CryptoExchangeProvider.Binance);
        _revolutX.SetupGet(a => a.ExchangeName).Returns(CryptoExchangeProvider.RevolutX);
    }

    private ConnectExchangeCommandHandler CreateHandler() =>
        new(
            _credentialRepo.Object,
            new CryptoExchangeAdapterRegistry([_binance.Object, _revolutX.Object]),
            _encryption.Object,
            _syncHandler.Object);

    private static EncryptionResult FakeEncryption() =>
        new(Ciphertext: [1], Iv: [2], AuthTag: [3], KeyVersion: 1);

    private static ExchangeCredential Credential(Guid userId, string provider) =>
        ExchangeCredential.Create(userId, provider, [1], [2], [3], [4], [5], [6], 1);

    private void GivenNoCredential(Guid userId, string provider) =>
        _credentialRepo
            .Setup(r => r.GetAsync(userId, provider, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExchangeCredential?)null);

    private void GivenValidationPasses(Mock<ICryptoExchangeAdapter> adapter) =>
        adapter
            .Setup(a => a.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

    private void GivenStoreAndSyncSucceed(Action<ExchangeCredential>? onAdd = null)
    {
        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns(FakeEncryption());
        _credentialRepo
            .Setup(r => r.AddAsync(It.IsAny<ExchangeCredential>(), It.IsAny<CancellationToken>()))
            .Callback<ExchangeCredential, CancellationToken>((c, _) => onAdd?.Invoke(c))
            .Returns(Task.CompletedTask);
        _credentialRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncExchangeHoldingsResult(0, DateTime.UtcNow));
    }

    [Fact]
    public async Task Handle_AlreadyConnected_ThrowsAlreadyConnected()
    {
        var userId = Guid.NewGuid();
        _credentialRepo
            .Setup(r => r.GetAsync(userId, CryptoExchangeProvider.Binance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Credential(userId, CryptoExchangeProvider.Binance));

        var act = () => CreateHandler().Handle(
            new ConnectExchangeCommand(userId, CryptoExchangeProvider.Binance, "key", "secret"), default);

        (await act.Should().ThrowAsync<ExchangeAlreadyConnectedException>())
            .Which.ErrorCode.Should().Be("ALREADY_CONNECTED");
    }

    [Fact]
    public async Task Handle_BinanceConnectedAlready_DoesNotBlockConnectingRevolutX()
    {
        // The credential lookup is per venue: a Binance row is invisible to a Revolut X connect.
        var userId = Guid.NewGuid();
        GivenNoCredential(userId, CryptoExchangeProvider.RevolutX);
        GivenValidationPasses(_revolutX);
        ExchangeCredential? saved = null;
        GivenStoreAndSyncSucceed(c => saved = c);

        var result = await CreateHandler().Handle(
            new ConnectExchangeCommand(userId, CryptoExchangeProvider.RevolutX, "revx-key", "-----BEGIN PRIVATE KEY-----"), default);

        saved!.Provider.Should().Be(CryptoExchangeProvider.RevolutX);
        result.Message.Should().Contain("Revolut X");
        _revolutX.Verify(a => a.ValidateCredentialsAsync("revx-key", "-----BEGIN PRIVATE KEY-----", It.IsAny<CancellationToken>()), Times.Once);
        _binance.Verify(a => a.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _syncHandler.Verify(
            h => h.Handle(new SyncExchangeHoldingsCommand(userId, CryptoExchangeProvider.RevolutX), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidCredentials_CallsValidateCredentials()
    {
        var userId = Guid.NewGuid();
        GivenNoCredential(userId, CryptoExchangeProvider.Binance);
        GivenValidationPasses(_binance);
        GivenStoreAndSyncSucceed();

        await CreateHandler().Handle(
            new ConnectExchangeCommand(userId, CryptoExchangeProvider.Binance, "mykey", "mysecret"), default);

        _binance.Verify(a => a.ValidateCredentialsAsync("mykey", "mysecret", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_VenueRejectsTheKey_StoresNothing()
    {
        var userId = Guid.NewGuid();
        GivenNoCredential(userId, CryptoExchangeProvider.RevolutX);
        _revolutX
            .Setup(a => a.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RevolutXException("Revolut X API error (HTTP 401): Unauthorized"));

        var act = () => CreateHandler().Handle(
            new ConnectExchangeCommand(userId, CryptoExchangeProvider.RevolutX, "key", "pem"), default);

        (await act.Should().ThrowAsync<RevolutXException>()).Which.StatusCode.Should().Be(422);
        _credentialRepo.Verify(r => r.AddAsync(It.IsAny<ExchangeCredential>(), It.IsAny<CancellationToken>()), Times.Never);
        _credentialRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _encryption.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidCredentials_EncryptsBothHalvesBeforeStoring()
    {
        var userId = Guid.NewGuid();
        GivenNoCredential(userId, CryptoExchangeProvider.Binance);
        GivenValidationPasses(_binance);
        ExchangeCredential? saved = null;
        GivenStoreAndSyncSucceed(c => saved = c);

        const string plaintextSecret = "super-secret-binance-api-secret";
        await CreateHandler().Handle(
            new ConnectExchangeCommand(userId, CryptoExchangeProvider.Binance, "key", plaintextSecret), default);

        saved.Should().NotBeNull();
        saved!.Provider.Should().Be(CryptoExchangeProvider.Binance);
        _encryption.Verify(e => e.Encrypt("key"), Times.Once);
        _encryption.Verify(e => e.Encrypt(plaintextSecret), Times.Once);
    }

    [Fact]
    public async Task Handle_PreviouslyDisconnected_ReactivatesTheSameRowWithTheNewKey()
    {
        // Unique on (UserId, Provider): inserting a second row for a disconnected venue would
        // violate the index, so a reconnect must reuse the row.
        var userId = Guid.NewGuid();
        var disconnected = Credential(userId, CryptoExchangeProvider.Binance);
        disconnected.MarkSyncFailed("old key revoked");
        disconnected.Deactivate();
        _credentialRepo
            .Setup(r => r.GetAsync(userId, CryptoExchangeProvider.Binance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(disconnected);
        GivenValidationPasses(_binance);
        GivenStoreAndSyncSucceed();
        _encryption.Setup(e => e.Encrypt(It.IsAny<string>()))
            .Returns(new EncryptionResult([9], [9], [9], KeyVersion: 2));
        _credentialRepo.Setup(r => r.Update(disconnected));

        await CreateHandler().Handle(
            new ConnectExchangeCommand(userId, CryptoExchangeProvider.Binance, "new-key", "new-secret"), default);

        disconnected.IsActive.Should().BeTrue();
        disconnected.EncryptedApiKey.Should().Equal(9);
        disconnected.KeyVersion.Should().Be(2);
        disconnected.LastSyncError.Should().BeNull();
        _credentialRepo.Verify(r => r.AddAsync(It.IsAny<ExchangeCredential>(), It.IsAny<CancellationToken>()), Times.Never);
        _credentialRepo.Verify(r => r.Update(disconnected), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownProvider_Throws()
    {
        var act = () => CreateHandler().Handle(
            new ConnectExchangeCommand(Guid.NewGuid(), "kraken", "key", "secret"), default);

        await act.Should().ThrowAsync<UnknownExchangeProviderException>();
    }

    [Fact]
    public void Command_ToString_NeverPrintsTheSecret()
    {
        var command = new ConnectExchangeCommand(Guid.NewGuid(), CryptoExchangeProvider.RevolutX, "api-key-value", "PRIVATE-KEY-VALUE");

        command.ToString().Should().NotContain("PRIVATE-KEY-VALUE").And.NotContain("api-key-value");
        new ConnectRevolutXRequest("api-key-value", "PRIVATE-KEY-VALUE").ToString()
            .Should().NotContain("PRIVATE-KEY-VALUE");
    }
}
