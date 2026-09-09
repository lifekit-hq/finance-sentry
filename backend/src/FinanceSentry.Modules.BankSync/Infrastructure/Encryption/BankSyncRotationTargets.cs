namespace FinanceSentry.Modules.BankSync.Infrastructure.Encryption;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Key rotation for <c>MonobankCredentials</c> (issue #493).
/// </summary>
public sealed class MonobankCredentialRotationTarget(
    BankSyncDbContext db,
    ICredentialEncryptionService encryption) : ICredentialRotationTarget
{
    public string Name => "MonobankCredentials";

    public async Task<int> RotateAsync(int targetKeyVersion, CancellationToken cancellationToken)
    {
        // Idempotent by query: an already-rotated store selects nothing and does no writes.
        var stale = await db.MonobankCredentials
            .Where(c => c.KeyVersion != targetKeyVersion)
            .ToListAsync(cancellationToken);

        foreach (var row in stale)
        {
            var plaintext = encryption.Decrypt(
                row.EncryptedToken, row.Iv, row.AuthTag, row.KeyVersion);
            var reencrypted = encryption.Encrypt(plaintext);

            row.RotateEncryption(
                reencrypted.Ciphertext, reencrypted.Iv, reencrypted.AuthTag, reencrypted.KeyVersion);

            // Saved per row: EF wraps one SaveChanges in a transaction, so a row either moves to
            // the new key completely or not at all. A failure part-way leaves earlier rows
            // migrated and later ones untouched — both states are valid and the next run resumes.
            await db.SaveChangesAsync(cancellationToken);
        }

        return stale.Count;
    }
}

/// <summary>
/// Key rotation for <c>TrueLayerConnections</c> (issue #493).
/// </summary>
public sealed class TrueLayerConnectionRotationTarget(
    BankSyncDbContext db,
    ICredentialEncryptionService encryption) : ICredentialRotationTarget
{
    public string Name => "TrueLayerConnections";

    public async Task<int> RotateAsync(int targetKeyVersion, CancellationToken cancellationToken)
    {
        var stale = await db.TrueLayerConnections
            .Where(c => c.KeyVersion != targetKeyVersion)
            .ToListAsync(cancellationToken);

        var rotated = 0;
        foreach (var row in stale)
        {
            // A connection can exist before finalize has ever stored a refresh token. There is
            // nothing to re-encrypt, and decrypting an empty payload would throw — so the row is
            // left alone rather than counted or crashed on. Its key version stays stale until it
            // holds a token, which is correct: it holds no secret.
            if (row.EncryptedRefreshToken.Length == 0)
            {
                continue;
            }

            var plaintext = encryption.Decrypt(
                row.EncryptedRefreshToken, row.Iv, row.AuthTag, row.KeyVersion);
            var reencrypted = encryption.Encrypt(plaintext);

            row.RotateEncryption(
                reencrypted.Ciphertext, reencrypted.Iv, reencrypted.AuthTag, reencrypted.KeyVersion);

            await db.SaveChangesAsync(cancellationToken);
            rotated++;
        }

        return rotated;
    }
}
