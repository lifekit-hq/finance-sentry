namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Domain;

public class TrueLayerConnection : Entity
{
    public Guid UserId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public string ProviderDisplayName { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public string Status { get; set; } = "CREATED";

    public byte[] EncryptedRefreshToken { get; private set; } = [];
    public byte[] Iv { get; private set; } = [];
    public byte[] AuthTag { get; private set; } = [];
    public int KeyVersion { get; private set; } = 1;

    public DateTime? ConnectionExpiresAt { get; set; }
    public DateTime? LastSyncAt { get; set; }

    public ICollection<BankAccount> BankAccounts { get; set; } = [];

    public TrueLayerConnection() { }

    public TrueLayerConnection(
        Guid userId,
        string providerId,
        string providerDisplayName,
        string reference)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("ProviderId cannot be empty.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Reference cannot be empty.", nameof(reference));

        UserId = userId;
        ProviderId = providerId;
        ProviderDisplayName = providerDisplayName;
        Reference = reference;
    }

    public void SetRefreshToken(byte[] ciphertext, byte[] iv, byte[] authTag)
    {
        EncryptedRefreshToken = ciphertext;
        Iv = iv;
        AuthTag = authTag;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkLinked(DateTime? expiresAt)
    {
        Status = "LINKED";
        ConnectionExpiresAt = expiresAt;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkExpired()
    {
        Status = "EXPIRED";
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reuses this connection row for a reconnect/reauth flow: assigns a fresh OAuth reference
    /// and returns the connection to a pending ("CREATED") state so <c>FinalizeTrueLayerConnect</c>
    /// will exchange the new authorization code. Linked accounts stay attached, and the existing
    /// refresh token is left in place until finalize swaps in the new one — so an <b>abandoned</b>
    /// consent does not sever a still-working connection.
    /// </summary>
    public void BeginReauth(string newReference)
    {
        if (string.IsNullOrWhiteSpace(newReference))
            throw new ArgumentException("Reference cannot be empty.", nameof(newReference));

        Reference = newReference;
        Status = "CREATED";
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Replaces this row's ciphertext with the same plaintext re-encrypted under a newer key
    /// (issue #493). The payload and the key version move together — a row is never left with new
    /// ciphertext and an old version, or the reverse. Not a business update: the credential is
    /// unchanged, so no domain timestamp moves.
    /// </summary>
    public void RotateEncryption(byte[] ciphertext, byte[] iv, byte[] authTag, int keyVersion)
    {
        EncryptedRefreshToken = ciphertext;
        Iv = iv;
        AuthTag = authTag;
        KeyVersion = keyVersion;
    }
}
