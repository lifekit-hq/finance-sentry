namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Domain;

public class MonobankCredential : Entity
{
    public Guid UserId { get; private set; }
    public byte[] EncryptedToken { get; private set; } = [];
    public byte[] Iv { get; private set; } = [];
    public byte[] AuthTag { get; private set; } = [];
    public int KeyVersion { get; private set; } = 1;
    public DateTime? LastSyncAt { get; set; }

    public ICollection<BankAccount> BankAccounts { get; set; } = [];

    public MonobankCredential() { }

    public MonobankCredential(Guid userId, byte[] encryptedToken, byte[] iv, byte[] authTag)
    {
        UserId = userId;
        EncryptedToken = encryptedToken;
        Iv = iv;
        AuthTag = authTag;
    }

    /// <summary>
    /// Replaces this row's ciphertext with the same plaintext re-encrypted under a newer key
    /// (issue #493). The payload and the key version move together — a row is never left with new
    /// ciphertext and an old version, or the reverse. Not a business update: the credential is
    /// unchanged, so no domain timestamp moves.
    /// </summary>
    public void RotateEncryption(byte[] encryptedToken, byte[] iv, byte[] authTag, int keyVersion)
    {
        EncryptedToken = encryptedToken;
        Iv = iv;
        AuthTag = authTag;
        KeyVersion = keyVersion;
    }
}
