using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.BrokerageSync.Application.Connect;

/// <summary>The handed-over session refreshed and is stored: the connection is active.</summary>
public sealed record InzhurConnectResult(string Status)
{
    public const string Connected = "connected";
}

/// <summary>The caller's Inzhur connection as the connect modal and the accounts list need it. Never carries a secret.</summary>
public sealed record InzhurConnectionStatusResult(string Status, DateTime? LastSyncAt, DateTime? SessionStartedAt)
{
    public const string NotConnected = "not_connected";
}

public interface IInzhurConnector
{
    /// <summary>
    /// Takes over the session the owner signed in to on inzhur.reit, given as the value of its refresh cookie: one
    /// refresh proves it before anything is stored, then the session is stored encrypted and read once.
    /// </summary>
    Task<InzhurConnectResult> ConnectSessionAsync(Guid userId, string? refreshCookie, CancellationToken ct);

    /// <summary>Forgets the connection, its session and its holdings. Inzhur itself is not called.</summary>
    Task DisconnectAsync(Guid userId, CancellationToken ct);

    Task<InzhurConnectionStatusResult> GetStatusAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// The owner-initiated half of the Inzhur sync. finance-sentry never signs in to Inzhur: its sign-in sits behind a
/// reCAPTCHA v3 that rejects this server's datacenter IP (data/fs-inzhur-recaptcha), so the owner signs in in his own
/// browser and hands over the session's refresh cookie. The refresh endpoint has no bot check; the daily job keeps the
/// session alive from there.
/// </summary>
public sealed class InzhurConnector(
    IInzhurCredentialRepository credentialRepository,
    IBrokerageHoldingRepository holdingRepository,
    IInzhurApiClient api,
    IInzhurSyncService syncService,
    ICredentialEncryptionService encryption,
    IAlertGeneratorService alerts,
    IOptions<InzhurOptions> options,
    TimeProvider clock,
    ILogger<InzhurConnector> logger) : IInzhurConnector
{
    private const string Provider = InzhurHoldingsMapper.Provider;

    /// <summary>Far above any real refresh token; a longer paste is not one.</summary>
    public const int MaxRefreshCookieLength = 8192;

    public async Task<InzhurConnectResult> ConnectSessionAsync(Guid userId, string? refreshCookie, CancellationToken ct)
    {
        var value = Normalize(refreshCookie);
        var handedOver = InzhurSession.FromRefreshCookie(options.Value.RefreshCookieName, value, new Uri(options.Value.AuthBaseUrl));

        InzhurSession session;
        try
        {
            session = await api.RefreshAsync(handedOver, ct);
        }
        catch (InzhurApiException ex)
        {
            // The kind only: the exception never carries the cookie, and neither does this line.
            logger.LogWarning("Inzhur refused the handed-over session for user {UserId}: {FailureKind}", userId, ex.Kind);
            throw ex.Kind switch
            {
                InzhurFailureKind.ReauthRequired => Error(StatusCodes.Status422UnprocessableEntity, InzhurErrorCodes.SessionRejected,
                    "Inzhur did not accept that session. Sign in on inzhur.reit again and copy a fresh refreshToken."),
                InzhurFailureKind.RateLimited or InzhurFailureKind.Unavailable => Error(StatusCodes.Status503ServiceUnavailable,
                    InzhurErrorCodes.Unavailable, "Inzhur is unavailable right now. Try again later."),
                _ => Error(StatusCodes.Status502BadGateway, InzhurErrorCodes.ConnectFailed, "Inzhur answered the session check unexpectedly."),
            };
        }

        var credential = await credentialRepository.GetByUserIdAsync(userId, ct);
        if (credential is null)
        {
            credential = new InzhurCredential(userId);
            await credentialRepository.AddAsync(credential, ct);
        }

        credential.StartSession(Encrypt(session.Serialize()), Now);
        await credentialRepository.SaveChangesAsync(ct);
        logger.LogInformation("Inzhur connected for user {UserId}", userId);
        await TryResolveAlertAsync(userId);
        await TrySyncAsync(userId, ct);
        return new InzhurConnectResult(InzhurConnectResult.Connected);
    }

    public async Task DisconnectAsync(Guid userId, CancellationToken ct)
    {
        var credential = await credentialRepository.GetByUserIdAsync(userId, ct)
            ?? throw Error(StatusCodes.Status404NotFound, InzhurErrorCodes.NotConnected, "No Inzhur connection to remove.");

        credentialRepository.Delete(credential);
        await holdingRepository.DeleteByUserIdAndProviderAsync(userId, Provider, ct);
        await credentialRepository.SaveChangesAsync(ct);
        await TryResolveAlertAsync(userId);
        logger.LogInformation("Inzhur connection removed for user {UserId}", userId);
    }

    public async Task<InzhurConnectionStatusResult> GetStatusAsync(Guid userId, CancellationToken ct)
    {
        var credential = await credentialRepository.GetByUserIdAsync(userId, ct);
        return credential is null
            ? new InzhurConnectionStatusResult(InzhurConnectionStatusResult.NotConnected, null, null)
            : new InzhurConnectionStatusResult(credential.Status, credential.LastSyncAt, credential.SessionStartedAt);
    }

    // The first read right after a hand-over, so the holdings show at once; a failure here leaves the connection in
    // place for the daily job.
    private async Task TrySyncAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            await syncService.SyncAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("First Inzhur read after connecting failed for user {UserId}: {ErrorType}", userId, ex.GetType().Name);
        }
    }

    private async Task TryResolveAlertAsync(Guid userId)
    {
        try
        {
            await alerts.ResolveSyncFailureAlertAsync(userId, Provider, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve the Inzhur sync alert for user {UserId}", userId);
        }
    }

    private static InzhurConnectException Error(int status, string code, string message) => new(status, code, message);

    // What the owner copied may carry the cookie's name or quotes around it (DevTools copies either way). What is left
    // must be a bare cookie value: no whitespace, separators or control characters.
    private string Normalize(string? pasted)
    {
        var value = (pasted ?? string.Empty).Trim();
        var prefix = options.Value.RefreshCookieName + "=";
        if (value.StartsWith(prefix, StringComparison.Ordinal))
            value = value[prefix.Length..].Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        if (value.Length == 0)
            throw Error(StatusCodes.Status400BadRequest, InzhurErrorCodes.SessionRequired, "Paste the value of the refreshToken cookie.");
        if (value.Length > MaxRefreshCookieLength || value.Any(c => c is ';' or ',' or '"' or '\\' || char.IsWhiteSpace(c) || char.IsControl(c)))
            throw Error(StatusCodes.Status400BadRequest, InzhurErrorCodes.SessionInvalid, "That is not a refreshToken cookie value. Copy only the Value column.");
        return value;
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private EncryptedSecret Encrypt(string plaintext)
    {
        var result = encryption.Encrypt(plaintext);
        return new EncryptedSecret(result.Ciphertext, result.Iv, result.AuthTag, result.KeyVersion);
    }
}
