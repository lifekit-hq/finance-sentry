using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Encryption;

/// <summary>Key rotation for <c>InzhurCredentials</c>: phone, password and session jar (mirrors <see cref="IBKRFlexCredentialRotationTarget"/>).</summary>
public sealed class InzhurCredentialRotationTarget(
    BrokerageSyncDbContext db,
    ICredentialEncryptionService encryption) : ICredentialRotationTarget
{
    public string Name => "InzhurCredentials";

    public async Task<int> RotateAsync(int targetKeyVersion, CancellationToken cancellationToken)
    {
        var stale = await db.InzhurCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.KeyVersion != targetKeyVersion || c.SessionKeyVersion != targetKeyVersion)
            .ToListAsync(cancellationToken);

        foreach (var row in stale)
        {
            var phone = row.HasLoginSecrets ? Reencrypt(row.EncryptedPhone, row.PhoneIv, row.PhoneAuthTag, row.KeyVersion) : null;
            var password = row.HasLoginSecrets ? Reencrypt(row.EncryptedPassword, row.PasswordIv, row.PasswordAuthTag, row.KeyVersion) : null;
            var session = row.HasSession ? Reencrypt(row.EncryptedSession, row.SessionIv, row.SessionAuthTag, row.SessionKeyVersion) : null;

            row.RotateEncryption(targetKeyVersion, phone, password, session);

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
