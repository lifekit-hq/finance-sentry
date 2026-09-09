namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Encryption;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Key rotation for <c>IBKRCredentials</c> (issue #493).
///
/// Three payloads per row — the access-token secret and the two RSA private keys — share one key
/// version, so all three are re-encrypted before the row is saved.
/// </summary>
public sealed class IBKRCredentialRotationTarget(
    BrokerageSyncDbContext db,
    ICredentialEncryptionService encryption) : ICredentialRotationTarget
{
    public string Name => "IBKRCredentials";

    public async Task<int> RotateAsync(int targetKeyVersion, CancellationToken cancellationToken)
    {
        var stale = await db.IBKRCredentials
            .Where(c => c.KeyVersion != targetKeyVersion)
            .ToListAsync(cancellationToken);

        foreach (var row in stale)
        {
            var accessTokenSecret = encryption.Decrypt(
                row.EncryptedAccessTokenSecret,
                row.AccessTokenSecretIv,
                row.AccessTokenSecretAuthTag,
                row.KeyVersion);
            var signatureKey = encryption.Decrypt(
                row.EncryptedSignatureKey,
                row.SignatureKeyIv,
                row.SignatureKeyAuthTag,
                row.KeyVersion);
            var encryptionKey = encryption.Decrypt(
                row.EncryptedEncryptionKey,
                row.EncryptionKeyIv,
                row.EncryptionKeyAuthTag,
                row.KeyVersion);

            var newAccessTokenSecret = encryption.Encrypt(accessTokenSecret);
            var newSignatureKey = encryption.Encrypt(signatureKey);
            var newEncryptionKey = encryption.Encrypt(encryptionKey);

            row.RotateEncryption(
                newAccessTokenSecret.Ciphertext,
                newAccessTokenSecret.Iv,
                newAccessTokenSecret.AuthTag,
                newSignatureKey.Ciphertext,
                newSignatureKey.Iv,
                newSignatureKey.AuthTag,
                newEncryptionKey.Ciphertext,
                newEncryptionKey.Iv,
                newEncryptionKey.AuthTag,
                newAccessTokenSecret.KeyVersion);

            await db.SaveChangesAsync(cancellationToken);
        }

        return stale.Count;
    }
}
