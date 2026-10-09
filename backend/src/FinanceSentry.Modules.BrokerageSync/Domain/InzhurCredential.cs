using FinanceSentry.Core.Connections;

namespace FinanceSentry.Modules.BrokerageSync.Domain;

/// <summary>
/// A user's own Inzhur cabinet connection. Inzhur has no API and its sign-in is guarded by a reCAPTCHA v3 that rejects
/// a server's datacenter IP, so the owner signs in on inzhur.reit in his own browser and pastes the session's refresh
/// cookie into finance-sentry. The daily sync then keeps that one session alive: it refreshes it and reads the
/// portfolio, read-only. finance-sentry never signs in, so it never holds the phone number or password.
///
/// One secret lives here, AES-256-GCM encrypted at rest (the IBKR path): the session jar (the cabinet's refresh cookie
/// and short-lived access token, replaced after every refresh in case it rotated). It is never logged.
/// </summary>
public sealed class InzhurCredential
{
    /// <summary>Column width of <see cref="LastSyncError"/>.</summary>
    public const int LastErrorMaxLength = 1000;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary><see cref="InzhurConnectionStatus.Active"/> or <see cref="InzhurConnectionStatus.ReauthRequired"/>.</summary>
    public string Status { get; private set; } = InzhurConnectionStatus.ReauthRequired;

    public DateTime CreatedAt { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public string? LastSyncError { get; private set; }

    /// <summary>When the current session was handed over by the owner, to measure how long one lives.</summary>
    public DateTime? SessionStartedAt { get; private set; }

    /// <summary>When the session was last refreshed successfully.</summary>
    public DateTime? SessionRefreshedAt { get; private set; }

    /// <summary>The serialized session jar; empty while no session is held.</summary>
    public byte[] EncryptedSession { get; private set; } = [];
    public byte[] SessionIv { get; private set; } = [];
    public byte[] SessionAuthTag { get; private set; } = [];

    /// <summary>Key version of the session ciphertext, which is re-encrypted on every refresh.</summary>
    public int SessionKeyVersion { get; private set; } = 1;

    /// <summary>
    /// Health of this connection by the connection-health policy. Shadow mode (Option B, S1): recorded on
    /// each sync, read by nothing that alerts yet.
    /// </summary>
    public ConnectionHealth Health { get; private set; } = new();

    public bool HasSession => EncryptedSession.Length > 0;

    private InzhurCredential() { }

    public InzhurCredential(Guid userId)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>The owner handed over a fresh session that refreshed: the connection is active again.</summary>
    public void StartSession(EncryptedSecret session, DateTime nowUtc)
    {
        SetSession(session);
        Status = InzhurConnectionStatus.Active;
        SessionStartedAt = nowUtc;
        SessionRefreshedAt = nowUtc;
        LastSyncError = null;
    }

    /// <summary>A refresh succeeded; the jar is replaced whether or not the cookie rotated.</summary>
    public void RefreshSession(EncryptedSecret session, DateTime nowUtc)
    {
        SetSession(session);
        SessionRefreshedAt = nowUtc;
    }

    /// <summary>The refresh chain broke: only the owner can hand over a new session. The dead session is dropped.</summary>
    public void MarkReauthRequired(string reason)
    {
        Status = InzhurConnectionStatus.ReauthRequired;
        EncryptedSession = [];
        SessionIv = [];
        SessionAuthTag = [];
        RecordSyncError(reason);
    }

    public void RecordSyncSuccess(DateTime nowUtc)
    {
        LastSyncAt = nowUtc;
        LastSyncError = null;
    }

    public void RecordSyncError(string error)
        => LastSyncError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;

    /// <summary>
    /// Replaces the session ciphertext with the same plaintext re-encrypted under <paramref name="keyVersion"/>
    /// (mirrors <see cref="IBKRFlexCredential.RotateEncryption"/>); null when the row holds no session. Not a business
    /// update.
    /// </summary>
    public void RotateEncryption(int keyVersion, EncryptedSecret? session)
    {
        if (session is not null)
            SetSession(session);
        else
            SessionKeyVersion = keyVersion;
    }

    public void ApplyHealth(ConnectionHealth health) => Health = health;

    private void SetSession(EncryptedSecret session)
        => (EncryptedSession, SessionIv, SessionAuthTag, SessionKeyVersion) =
            (session.Ciphertext, session.Iv, session.AuthTag, session.KeyVersion);
}

/// <summary>One AES-GCM ciphertext with its nonce, tag and key version.</summary>
public sealed record EncryptedSecret(byte[] Ciphertext, byte[] Iv, byte[] AuthTag, int KeyVersion);

public static class InzhurConnectionStatus
{
    public const string Active = "active";

    /// <summary>Same value BankSync uses for a TrueLayer consent that needs the owner, so the UI treats both alike.</summary>
    public const string ReauthRequired = "reauth_required";
}
