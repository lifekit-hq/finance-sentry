using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Connections;

namespace FinanceSentry.Modules.CryptoSync.Domain;

/// <summary>
/// One user's API credential for one crypto venue, keyed on <c>(UserId, Provider)</c> (#472).
///
/// Every venue authenticates with a public key id plus a secret: Binance with an HMAC API secret,
/// Revolut X with an Ed25519 private key (PEM). Both halves are encrypted at rest under the same
/// key version, so they rotate together.
/// </summary>
public sealed class ExchangeCredential : IUserOwned
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

    /// <summary>When this user first linked the venue. Set once; <see cref="Reconnect"/> re-stamps <see cref="CreatedAt"/> but never this.</summary>
    public DateTime FirstLinkedAt { get; private set; }

    /// <summary>
    /// How far the venue's money movements (<see cref="CryptoFlowEvent"/>) have been read, from
    /// <see cref="FirstLinkedAt"/> on; null until the first walk succeeds. A run can read only part of the gap, or be
    /// held back by a pending movement, so this alone does not say the flows are known: they are once it is close
    /// behind now.
    /// </summary>
    public DateTime? FlowsWalkedTo { get; private set; }

    /// <summary>Which of the venue's movement sources the next walk reads, for a venue that reads one source per run.</summary>
    public int FlowsWalkLane { get; private set; }

    /// <summary>
    /// How far the venue's on-chain deposits and withdrawals have been read, for a venue that walks them apart from
    /// its other movements (Binance, S2b), from <see cref="FirstLinkedAt"/> on; null until the first such walk
    /// succeeds. Its own cursor, so the days <see cref="FlowsWalkedTo"/> passed before S2b are read too.
    /// </summary>
    public DateTime? ChainFlowsWalkedTo { get; private set; }

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

        var now = DateTime.UtcNow;
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
            CreatedAt = now,
            FirstLinkedAt = now,
        };
    }

    public void MarkSynced(DateTime syncedAt)
    {
        LastSyncAt = syncedAt;
        LastSyncError = null;
    }

    public void MarkFlowsWalked(DateTime walkedTo, int nextLane = 0)
    {
        FlowsWalkedTo = walkedTo;
        FlowsWalkLane = nextLane;
    }

    public void MarkChainFlowsWalked(DateTime walkedTo)
    {
        ChainFlowsWalkedTo = walkedTo;
    }

    private const int MaxSyncErrorLength = 1000;

    public void MarkSyncFailed(string error)
    {
        LastSyncError = error.Length > MaxSyncErrorLength ? error[..MaxSyncErrorLength] : error;
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
        FlowsWalkedTo = null;
        FlowsWalkLane = 0;
        ChainFlowsWalkedTo = null;
        CreatedAt = DateTime.UtcNow;
        Health = Health with { LastFailureAt = null };
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

    /// <summary>
    /// Health of this connection by the connection-health policy. Shadow mode (Option B, S1): recorded on
    /// each sync, read by nothing that alerts yet.
    /// </summary>
    public ConnectionHealth Health { get; private set; } = new();

    public void ApplyHealth(ConnectionHealth health) => Health = health;
}
