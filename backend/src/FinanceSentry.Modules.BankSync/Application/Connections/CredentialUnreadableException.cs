namespace FinanceSentry.Modules.BankSync.Application.Connections;

using System.Security.Cryptography;
using FinanceSentry.Infrastructure.Encryption;

/// <summary>
/// The stored bank credential could not be decrypted on our side: its authentication tag fails, its key version is
/// not configured, or the row is malformed. The provider never saw a request, but the connection cannot work until
/// the user enters the credential again, so the connection-health policy reads it as a definitive credential loss.
/// </summary>
/// <remarks>It keeps the decryption failure's message, so a sync still in shadow mode records the same text as before.</remarks>
public sealed class CredentialUnreadableException(Exception inner) : Exception(inner.Message, inner)
{
    /// <summary>The error code the failed attempt is recorded with.</summary>
    public const string Code = "CREDENTIAL_UNREADABLE";

    /// <summary>Decrypts a stored credential, turning a failure to read it into <see cref="CredentialUnreadableException"/>.</summary>
    public static string Decrypt(ICredentialEncryptionService encryption, byte[] ciphertext, byte[] iv, byte[] authTag, int keyVersion)
    {
        try
        {
            return encryption.Decrypt(ciphertext, iv, authTag, keyVersion);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or InvalidOperationException)
        {
            throw new CredentialUnreadableException(ex);
        }
    }
}
