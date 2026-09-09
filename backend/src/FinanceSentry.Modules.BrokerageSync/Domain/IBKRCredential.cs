namespace FinanceSentry.Modules.BrokerageSync.Domain;

/// <summary>
/// Represents a single user's link to an IBKR account via OAuth 1.0a
/// self-service.
///
/// Each user registers their own consumer key + access token on IBKR's
/// self-service OAuth portal and generates three PEM artifacts locally. The two
/// RSA private keys and the access token secret are stored encrypted at rest
/// (AES-256-GCM); the consumer key, access token, and (public) Diffie-Hellman
/// parameters are stored in the clear. The backend derives a daily live session
/// token from these and signs every IBKR request — no username/password, no
/// IBeam container, no 2FA prompt, and no session conflict with the IBKR mobile
/// app.
/// </summary>
public sealed class IBKRCredential
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string? AccountId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public string? LastSyncError { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>The 9-character consumer key registered on the IBKR portal (identifier, not secret).</summary>
    public string ConsumerKey { get; private set; } = string.Empty;

    /// <summary>The OAuth access token (identifier, not secret).</summary>
    public string AccessToken { get; private set; } = string.Empty;

    /// <summary>Diffie-Hellman parameters (public) seeding the live-session-token exchange.</summary>
    public string DhParam { get; private set; } = string.Empty;

    public byte[] EncryptedAccessTokenSecret { get; private set; } = [];
    public byte[] AccessTokenSecretIv { get; private set; } = [];
    public byte[] AccessTokenSecretAuthTag { get; private set; } = [];

    public byte[] EncryptedSignatureKey { get; private set; } = [];
    public byte[] SignatureKeyIv { get; private set; } = [];
    public byte[] SignatureKeyAuthTag { get; private set; } = [];

    public byte[] EncryptedEncryptionKey { get; private set; } = [];
    public byte[] EncryptionKeyIv { get; private set; } = [];
    public byte[] EncryptionKeyAuthTag { get; private set; } = [];

    public int KeyVersion { get; private set; } = 1;

    private IBKRCredential() { }

    public IBKRCredential(
        Guid userId,
        string consumerKey,
        string accessToken,
        string dhParam,
        byte[] encryptedAccessTokenSecret,
        byte[] accessTokenSecretIv,
        byte[] accessTokenSecretAuthTag,
        byte[] encryptedSignatureKey,
        byte[] signatureKeyIv,
        byte[] signatureKeyAuthTag,
        byte[] encryptedEncryptionKey,
        byte[] encryptionKeyIv,
        byte[] encryptionKeyAuthTag,
        int keyVersion)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        ConsumerKey = consumerKey;
        AccessToken = accessToken;
        DhParam = dhParam;
        EncryptedAccessTokenSecret = encryptedAccessTokenSecret;
        AccessTokenSecretIv = accessTokenSecretIv;
        AccessTokenSecretAuthTag = accessTokenSecretAuthTag;
        EncryptedSignatureKey = encryptedSignatureKey;
        SignatureKeyIv = signatureKeyIv;
        SignatureKeyAuthTag = signatureKeyAuthTag;
        EncryptedEncryptionKey = encryptedEncryptionKey;
        EncryptionKeyIv = encryptionKeyIv;
        EncryptionKeyAuthTag = encryptionKeyAuthTag;
        KeyVersion = keyVersion;
    }

    public void UpdateAccountId(string accountId) => AccountId = accountId;

    public void RecordSyncSuccess()
    {
        LastSyncAt = DateTime.UtcNow;
        LastSyncError = null;
    }

    public void RecordSyncError(string error) => LastSyncError = error;

    public void Deactivate() => IsActive = false;

    public void Reactivate(
        string consumerKey,
        string accessToken,
        string dhParam,
        byte[] encryptedAccessTokenSecret,
        byte[] accessTokenSecretIv,
        byte[] accessTokenSecretAuthTag,
        byte[] encryptedSignatureKey,
        byte[] signatureKeyIv,
        byte[] signatureKeyAuthTag,
        byte[] encryptedEncryptionKey,
        byte[] encryptionKeyIv,
        byte[] encryptionKeyAuthTag,
        int keyVersion)
    {
        IsActive = true;
        AccountId = null;
        LastSyncAt = null;
        LastSyncError = null;
        ConsumerKey = consumerKey;
        AccessToken = accessToken;
        DhParam = dhParam;
        EncryptedAccessTokenSecret = encryptedAccessTokenSecret;
        AccessTokenSecretIv = accessTokenSecretIv;
        AccessTokenSecretAuthTag = accessTokenSecretAuthTag;
        EncryptedSignatureKey = encryptedSignatureKey;
        SignatureKeyIv = signatureKeyIv;
        SignatureKeyAuthTag = signatureKeyAuthTag;
        EncryptedEncryptionKey = encryptedEncryptionKey;
        EncryptionKeyIv = encryptionKeyIv;
        EncryptionKeyAuthTag = encryptionKeyAuthTag;
        KeyVersion = keyVersion;
    }

    /// <summary>
    /// Replaces this row's ciphertext with the same plaintext re-encrypted under a newer key
    /// (issue #493). The payload and the key version move together — a row is never left with new
    /// ciphertext and an old version, or the reverse. Not a business update: the credential is
    /// unchanged, so no domain timestamp moves.
    /// </summary>
    public void RotateEncryption(
        byte[] encryptedAccessTokenSecret, byte[] accessTokenSecretIv, byte[] accessTokenSecretAuthTag,
        byte[] encryptedSignatureKey, byte[] signatureKeyIv, byte[] signatureKeyAuthTag,
        byte[] encryptedEncryptionKey, byte[] encryptionKeyIv, byte[] encryptionKeyAuthTag,
        int keyVersion)
    {
        EncryptedAccessTokenSecret = encryptedAccessTokenSecret;
        AccessTokenSecretIv = accessTokenSecretIv;
        AccessTokenSecretAuthTag = accessTokenSecretAuthTag;
        EncryptedSignatureKey = encryptedSignatureKey;
        SignatureKeyIv = signatureKeyIv;
        SignatureKeyAuthTag = signatureKeyAuthTag;
        EncryptedEncryptionKey = encryptedEncryptionKey;
        EncryptionKeyIv = encryptionKeyIv;
        EncryptionKeyAuthTag = encryptionKeyAuthTag;
        KeyVersion = keyVersion;
    }
}
