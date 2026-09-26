namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Encryption;

using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>Key rotation for <c>IBKRFlexCredentials</c> (mirrors <see cref="IBKRCredentialRotationTarget"/>).</summary>
public sealed class IBKRFlexCredentialRotationTarget(
    BrokerageSyncDbContext db,
    ICredentialEncryptionService encryption) : ICredentialRotationTarget
{
    public string Name => "IBKRFlexCredentials";

    public async Task<int> RotateAsync(int targetKeyVersion, CancellationToken cancellationToken)
    {
        var stale = await db.IBKRFlexCredentials
            .Where(c => c.KeyVersion != targetKeyVersion)
            .ToListAsync(cancellationToken);

        foreach (var row in stale)
        {
            var token = encryption.Decrypt(row.EncryptedToken, row.TokenIv, row.TokenAuthTag, row.KeyVersion);
            var newToken = encryption.Encrypt(token);

            row.RotateEncryption(newToken.Ciphertext, newToken.Iv, newToken.AuthTag, newToken.KeyVersion);

            await db.SaveChangesAsync(cancellationToken);
        }

        return stale.Count;
    }
}
