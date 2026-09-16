using FinanceSentry.Core.Cqrs;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;

namespace FinanceSentry.Modules.CryptoSync.Application.Commands;

public sealed record ConnectBinanceRequest(string ApiKey, string ApiSecret)
{
    public override string ToString() => nameof(ConnectBinanceRequest);
}

/// <summary>
/// A read-only Revolut X API key and the Ed25519 private key (PKCS#8 PEM) whose public half was
/// registered with it.
/// </summary>
public sealed record ConnectRevolutXRequest(string ApiKey, string PrivateKey)
{
    public override string ToString() => nameof(ConnectRevolutXRequest);
}

/// <summary>
/// Connects one crypto venue. <paramref name="ApiSecret"/> is whatever that venue signs with —
/// Binance's HMAC secret, Revolut X's Ed25519 private key.
/// </summary>
public sealed record ConnectExchangeCommand(
    Guid UserId,
    string Provider,
    string ApiKey,
    string ApiSecret) : ICommand<ConnectExchangeResult>
{
    // Never let a stray log line print the secret.
    public override string ToString() =>
        $"{nameof(ConnectExchangeCommand)} {{ UserId = {UserId}, Provider = {Provider} }}";
}

public sealed record ConnectExchangeResult(
    string Message,
    int HoldingsCount,
    DateTime SyncedAt);

public sealed class ConnectExchangeCommandHandler(
    IExchangeCredentialRepository credentialRepository,
    CryptoExchangeAdapterRegistry adapters,
    ICredentialEncryptionService encryption,
    ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult> syncHandler)
    : ICommandHandler<ConnectExchangeCommand, ConnectExchangeResult>
{
    public async Task<ConnectExchangeResult> Handle(ConnectExchangeCommand command, CancellationToken cancellationToken)
    {
        var adapter = adapters.Get(command.Provider);

        var existing = await credentialRepository.GetAsync(command.UserId, command.Provider, cancellationToken);
        if (existing is not null && existing.IsActive)
        {
            throw new ExchangeAlreadyConnectedException(command.Provider);
        }

        // Validated against the live venue before anything is stored.
        await adapter.ValidateCredentialsAsync(command.ApiKey, command.ApiSecret, cancellationToken);

        var encryptedKey = encryption.Encrypt(command.ApiKey);
        var encryptedSecret = encryption.Encrypt(command.ApiSecret);

        if (existing is null)
        {
            await credentialRepository.AddAsync(
                ExchangeCredential.Create(
                    command.UserId,
                    command.Provider,
                    encryptedKey.Ciphertext,
                    encryptedKey.Iv,
                    encryptedKey.AuthTag,
                    encryptedSecret.Ciphertext,
                    encryptedSecret.Iv,
                    encryptedSecret.AuthTag,
                    encryptedKey.KeyVersion),
                cancellationToken);
        }
        else
        {
            // A disconnected venue keeps its row (unique on UserId + Provider), so reconnecting
            // replaces that row's key pair rather than inserting a second one.
            existing.Reconnect(
                encryptedKey.Ciphertext,
                encryptedKey.Iv,
                encryptedKey.AuthTag,
                encryptedSecret.Ciphertext,
                encryptedSecret.Iv,
                encryptedSecret.AuthTag,
                encryptedKey.KeyVersion);
            credentialRepository.Update(existing);
        }

        await credentialRepository.SaveChangesAsync(cancellationToken);

        var syncResult = await syncHandler.Handle(
            new SyncExchangeHoldingsCommand(command.UserId, command.Provider),
            cancellationToken);

        return new ConnectExchangeResult(
            $"{CryptoExchangeProvider.DisplayName(command.Provider)} account connected successfully.",
            syncResult.HoldingsCount,
            syncResult.SyncedAt);
    }
}
