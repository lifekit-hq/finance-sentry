namespace FinanceSentry.Modules.CryptoSync.Domain;

/// <summary>
/// One user's API credential for one crypto venue, keyed on <c>(UserId, Provider)</c> (#472).
///
/// Every venue authenticates with a public key id plus a secret: Binance with an HMAC API secret,
/// Revolut X with an Ed25519 private key (PEM). Both halves are encrypted at rest under the same
/// key version, so they rotate together.
/// </summary>
public sealed class ExchangeCredential
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public byte[] EncryptedApiKey { get; private set; } = [];
    public byte[] ApiKeyIv { get; private set; } = [];
    public byte[] ApiKeyAuthTag { get; private set; } = [];
    public byte[] EncryptedApiSecret { get; private set; } = [];
    public byte[] ApiSecretIv { get; private set; } = [];
    public byte[] ApiSecretAuthTag { get; private set; } = [];
    public int KeyVersion { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public string? LastSyncError { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ExchangeCredential() { }

    public static ExchangeCredential Create(
        Guid userId,
        string provider,
        byte[] encryptedApiKey,
        byte[] apiKeyIv,
        byte[] apiKeyAuthTag,
        byte[] encryptedApiSecret,
        byte[] apiSecretIv,
        byte[] apiSecretAuthTag,
        int keyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        return new ExchangeCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            EncryptedApiKey = encryptedApiKey,
            ApiKeyIv = apiKeyIv,
            ApiKeyAuthTag = apiKeyAuthTag,
            EncryptedApiSecret = encryptedApiSecret,
            ApiSecretIv = apiSecretIv,
            ApiSecretAuthTag = apiSecretAuthTag,
            KeyVersion = keyVersion,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public void MarkSynced(DateTime syncedAt)
    {
        LastSyncAt = syncedAt;
        LastSyncError = null;
    }

    public void MarkSyncFailed(string error)
    {
        LastSyncError = error;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Reactivates a disconnected credential with a new key pair. The previous sync state belonged
    /// to the previous key, so it is cleared.
    /// </summary>
    public void Reconnect(
        byte[] encryptedApiKey, byte[] apiKeyIv, byte[] apiKeyAuthTag,
        byte[] encryptedApiSecret, byte[] apiSecretIv, byte[] apiSecretAuthTag,
        int keyVersion)
    {
        RotateEncryption(
            encryptedApiKey, apiKeyIv, apiKeyAuthTag,
            encryptedApiSecret, apiSecretIv, apiSecretAuthTag,
            keyVersion);
        IsActive = true;
        LastSyncAt = null;
        LastSyncError = null;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Replaces this row's ciphertext with the same plaintext re-encrypted under a newer key
    /// (issue #493). The payload and the key version move together — a row is never left with new
    /// ciphertext and an old version, or the reverse. Not a business update: the credential is
    /// unchanged, so no domain timestamp moves.
    /// </summary>
    public void RotateEncryption(
        byte[] encryptedApiKey, byte[] apiKeyIv, byte[] apiKeyAuthTag,
        byte[] encryptedApiSecret, byte[] apiSecretIv, byte[] apiSecretAuthTag,
        int keyVersion)
    {
        EncryptedApiKey = encryptedApiKey;
        ApiKeyIv = apiKeyIv;
        ApiKeyAuthTag = apiKeyAuthTag;
        EncryptedApiSecret = encryptedApiSecret;
        ApiSecretIv = apiSecretIv;
        ApiSecretAuthTag = apiSecretAuthTag;
        KeyVersion = keyVersion;
    }
}
