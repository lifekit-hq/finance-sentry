using FinanceSentry.Core.Connections;

namespace FinanceSentry.Modules.BrokerageSync.Domain;

/// <summary>
/// A user's own Inzhur cabinet connection. Inzhur has no API, so finance-sentry signs in the way the web
/// cabinet does (phone + password + an SMS code the owner types into finance-sentry's reconnect modal) and
/// then keeps that one session alive: the daily sync refreshes it and reads the portfolio, read-only.
///
/// Three secrets live here, each AES-256-GCM encrypted at rest under the same key version (the IBKR path):
/// the phone number and password (so a reconnect asks only for the SMS code) and the session jar (the
/// cabinet's refresh cookie and short-lived access token, replaced after every refresh in case it rotated).
/// None of them is ever logged.
/// </summary>
public sealed class InzhurCredential
{
    /// <summary>Column width of <see cref="LastSyncError"/>.</summary>
    public const int LastErrorMaxLength = 1000;

    /// <summary>Login attempts allowed per UTC day; each one may send the owner an SMS.</summary>
    public const int MaxLoginAttemptsPerDay = 2;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary><see cref="InzhurConnectionStatus.Active"/> or <see cref="InzhurConnectionStatus.ReauthRequired"/>.</summary>
    public string Status { get; private set; } = InzhurConnectionStatus.ReauthRequired;

    public DateTime CreatedAt { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public string? LastSyncError { get; private set; }

    /// <summary>When the current session was obtained by a login, to measure how long one lives.</summary>
    public DateTime? SessionStartedAt { get; private set; }

    /// <summary>When the session was last refreshed successfully.</summary>
    public DateTime? SessionRefreshedAt { get; private set; }

    /// <summary>The UTC day <see cref="LoginAttempts"/> counts.</summary>
    public DateOnly? LoginAttemptsDay { get; private set; }
    public int LoginAttempts { get; private set; }

    public byte[] EncryptedPhone { get; private set; } = [];
    public byte[] PhoneIv { get; private set; } = [];
    public byte[] PhoneAuthTag { get; private set; } = [];

    public byte[] EncryptedPassword { get; private set; } = [];
    public byte[] PasswordIv { get; private set; } = [];
    public byte[] PasswordAuthTag { get; private set; } = [];

    /// <summary>The serialized session jar; empty while no session is held.</summary>
    public byte[] EncryptedSession { get; private set; } = [];
    public byte[] SessionIv { get; private set; } = [];
    public byte[] SessionAuthTag { get; private set; } = [];

    /// <summary>Key version of the phone and password ciphertexts.</summary>
    public int KeyVersion { get; private set; } = 1;

    /// <summary>Key version of the session ciphertext, which is re-encrypted on every refresh.</summary>
    public int SessionKeyVersion { get; private set; } = 1;

    /// <summary>
    /// Health of this connection by the connection-health policy. Shadow mode (Option B, S1): recorded on
    /// each sync, read by nothing that alerts yet.
    /// </summary>
    public ConnectionHealth Health { get; private set; } = new();

    public bool HasSession => EncryptedSession.Length > 0;

    /// <summary>Whether a phone and password are saved, so a reconnect needs only the SMS code.</summary>
    public bool HasLoginSecrets => EncryptedPhone.Length > 0 && EncryptedPassword.Length > 0;

    private InzhurCredential() { }

    public InzhurCredential(Guid userId, EncryptedSecret phone, EncryptedSecret password)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CreatedAt = DateTime.UtcNow;
        SetLoginSecrets(phone, password);
    }

    /// <summary>Stores the phone/password a successful login used (the owner may have typed new ones).</summary>
    public void SetLoginSecrets(EncryptedSecret phone, EncryptedSecret password)
    {
        (EncryptedPhone, PhoneIv, PhoneAuthTag) = (phone.Ciphertext, phone.Iv, phone.AuthTag);
        (EncryptedPassword, PasswordIv, PasswordAuthTag) = (password.Ciphertext, password.Iv, password.AuthTag);
        KeyVersion = phone.KeyVersion;
    }

    /// <summary>Inzhur rejected the saved phone/password: forget them so the next connect asks for both.</summary>
    public void ClearLoginSecrets()
    {
        (EncryptedPhone, PhoneIv, PhoneAuthTag) = ([], [], []);
        (EncryptedPassword, PasswordIv, PasswordAuthTag) = ([], [], []);
    }

    /// <summary>Whether another login may be attempted today; counts it when it may.</summary>
    public bool TryCountLoginAttempt(DateTime nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        if (LoginAttemptsDay != today)
        {
            LoginAttemptsDay = today;
            LoginAttempts = 0;
        }

        if (LoginAttempts >= MaxLoginAttemptsPerDay)
            return false;

        LoginAttempts++;
        return true;
    }

    /// <summary>A login produced a fresh session: the connection is active again.</summary>
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

    /// <summary>The refresh chain broke: only the owner can sign in again. The dead session is dropped.</summary>
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
    /// Replaces this row's ciphertexts with the same plaintexts re-encrypted under <paramref name="keyVersion"/>
    /// (mirrors <see cref="IBKRFlexCredential.RotateEncryption"/>); a secret the row does not hold is passed as null.
    /// Not a business update.
    /// </summary>
    public void RotateEncryption(int keyVersion, EncryptedSecret? phone, EncryptedSecret? password, EncryptedSecret? session)
    {
        if (phone is not null && password is not null)
            SetLoginSecrets(phone, password);
        KeyVersion = keyVersion;
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
