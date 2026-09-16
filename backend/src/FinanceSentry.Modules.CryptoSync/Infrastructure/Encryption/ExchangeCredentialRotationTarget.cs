namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Encryption;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Key rotation for <c>ExchangeCredentials</c> — every venue's credentials, Binance and Revolut X
/// alike (issues #493, #472).
///
/// Two payloads per row — API key and API secret (for Revolut X, the Ed25519 private key) — share
/// one key version, so both are re-encrypted before the row is saved. Rotating one and not the
/// other would leave the row undecryptable under either version.
/// </summary>
public sealed class ExchangeCredentialRotationTarget(
    CryptoSyncDbContext db,
    ICredentialEncryptionService encryption) : ICredentialRotationTarget
{
    public string Name => "ExchangeCredentials";

    public async Task<int> RotateAsync(int targetKeyVersion, CancellationToken cancellationToken)
    {
        var stale = await db.ExchangeCredentials
            .Where(c => c.KeyVersion != targetKeyVersion)
            .ToListAsync(cancellationToken);

        foreach (var row in stale)
        {
            var apiKey = encryption.Decrypt(
                row.EncryptedApiKey, row.ApiKeyIv, row.ApiKeyAuthTag, row.KeyVersion);
            var apiSecret = encryption.Decrypt(
                row.EncryptedApiSecret, row.ApiSecretIv, row.ApiSecretAuthTag, row.KeyVersion);

            var newApiKey = encryption.Encrypt(apiKey);
            var newApiSecret = encryption.Encrypt(apiSecret);

            row.RotateEncryption(
                newApiKey.Ciphertext, newApiKey.Iv, newApiKey.AuthTag,
                newApiSecret.Ciphertext, newApiSecret.Iv, newApiSecret.AuthTag,
                newApiKey.KeyVersion);

            await db.SaveChangesAsync(cancellationToken);
        }

        return stale.Count;
    }
}
