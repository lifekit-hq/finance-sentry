namespace FinanceSentry.Modules.CryptoSync.Domain;

public sealed class BinanceCredential
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
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

    private BinanceCredential() { }

    public static BinanceCredential Create(
        Guid userId,
        byte[] encryptedApiKey,
        byte[] apiKeyIv,
        byte[] apiKeyAuthTag,
        byte[] encryptedApiSecret,
        byte[] apiSecretIv,
        byte[] apiSecretAuthTag,
        int keyVersion)
    {
        return new BinanceCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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
