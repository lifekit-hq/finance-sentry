using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Encryption;

/// <summary>Key rotation for <c>InzhurCredentials</c>: the session jar (mirrors <see cref="IBKRFlexCredentialRotationTarget"/>).</summary>
public sealed class InzhurCredentialRotationTarget(
    BrokerageSyncDbContext db,
    ICredentialEncryptionService encryption) : ICredentialRotationTarget
{
    public string Name => "InzhurCredentials";

    public async Task<int> RotateAsync(int targetKeyVersion, CancellationToken cancellationToken)
    {
        var stale = await db.InzhurCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.SessionKeyVersion != targetKeyVersion)
            .ToListAsync(cancellationToken);

        foreach (var row in stale)
        {
            var session = row.HasSession ? Reencrypt(row.EncryptedSession, row.SessionIv, row.SessionAuthTag, row.SessionKeyVersion) : null;

            row.RotateEncryption(targetKeyVersion, session);

            await db.SaveChangesAsync(cancellationToken);
        }

        return stale.Count;
    }

    private EncryptedSecret Reencrypt(byte[] ciphertext, byte[] iv, byte[] authTag, int keyVersion)
    {
        var result = encryption.Encrypt(encryption.Decrypt(ciphertext, iv, authTag, keyVersion));
        return new EncryptedSecret(result.Ciphertext, result.Iv, result.AuthTag, result.KeyVersion);
    }
}
