using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Application.Connect;

/// <summary>Where a connect step left the caller: signed in, or waiting for the SMS code (possibly after a wrong one).</summary>
public sealed record InzhurConnectResult(string Status, DateTime? CodeExpiresAt = null, int? AttemptsLeft = null)
{
    public const string Connected = "connected";
    public const string CodeRequired = "code_required";
    public const string InvalidCode = "invalid_code";
}

/// <summary>The caller's Inzhur connection as the connect modal and the accounts list need it. Never carries a secret.</summary>
public sealed record InzhurConnectionStatusResult(
    string Status,
    bool HasSavedCredentials,
    DateTime? LastSyncAt,
    DateTime? SessionStartedAt,
    bool LoginAvailable)
{
    public const string NotConnected = "not_connected";
}

public interface IInzhurConnector
{
    /// <summary>
    /// Signs in with the typed phone/password, or the saved ones when none are typed. Inzhur then either signs in at
    /// once or sends the owner an SMS; at most <see cref="InzhurCredential.MaxLoginAttemptsPerDay"/> starts a day.
    /// </summary>
    Task<InzhurConnectResult> StartAsync(Guid userId, string? phone, string? password, CancellationToken ct);

    /// <summary>Submits the SMS code for the waiting sign-in; on success the session is stored and read once.</summary>
    Task<InzhurConnectResult> VerifyAsync(Guid userId, string code, CancellationToken ct);

    /// <summary>Forgets the connection, its secrets and its holdings. Inzhur itself is not called.</summary>
    Task DisconnectAsync(Guid userId, CancellationToken ct);

    Task<InzhurConnectionStatusResult> GetStatusAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// The owner-initiated half of the Inzhur sync (design report §3, "Door C"): the only path that signs in, and so the
/// only path that can make Inzhur send an SMS. The daily job never comes here.
/// </summary>
public sealed class InzhurConnector(
    IInzhurCredentialRepository credentialRepository,
    IBrokerageHoldingRepository holdingRepository,
    IInzhurBrowserLogin browserLogin,
    IInzhurSyncService syncService,
    ICredentialEncryptionService encryption,
    IAlertGeneratorService alerts,
    TimeProvider clock,
    ILogger<InzhurConnector> logger) : IInzhurConnector
{
    private const string Provider = InzhurHoldingsMapper.Provider;

    public async Task<InzhurConnectResult> StartAsync(Guid userId, string? phone, string? password, CancellationToken ct)
    {
        if (!browserLogin.IsAvailable)
            throw Error(StatusCodes.Status503ServiceUnavailable, InzhurErrorCodes.LoginUnavailable, "Inzhur sign-in is not available on this server.");

        var credential = await credentialRepository.GetByUserIdAsync(userId, ct);
        var identifier = Digits(phone);
        var typed = identifier.Length > 0 && !string.IsNullOrEmpty(password);

        if (typed)
        {
            if (credential is null)
            {
                credential = new InzhurCredential(userId, Encrypt(identifier), Encrypt(password!));
                await credentialRepository.AddAsync(credential, ct);
            }
            else
            {
                credential.SetLoginSecrets(Encrypt(identifier), Encrypt(password!));
            }
        }
        else if (credential is null || !credential.HasLoginSecrets)
        {
            throw Error(StatusCodes.Status400BadRequest, InzhurErrorCodes.CredentialsRequired, "Enter the phone number and password of the Inzhur account.");
        }
        else
        {
            identifier = Decrypt(credential.EncryptedPhone, credential.PhoneIv, credential.PhoneAuthTag, credential.KeyVersion);
            password = Decrypt(credential.EncryptedPassword, credential.PasswordIv, credential.PasswordAuthTag, credential.KeyVersion);
        }

        // Counted and stored before Inzhur is called, so a crash mid-sign-in still uses up the attempt.
        var allowed = credential.TryCountLoginAttempt(Now);
        await credentialRepository.SaveChangesAsync(ct);
        if (!allowed)
            throw Error(StatusCodes.Status429TooManyRequests, InzhurErrorCodes.LoginLimit, "Today's Inzhur sign-in attempts are used up. Try again tomorrow.");

        var outcome = await browserLogin.StartAsync(userId, identifier, password!, ct);
        return await ApplyAsync(credential, outcome, ct);
    }

    public async Task<InzhurConnectResult> VerifyAsync(Guid userId, string code, CancellationToken ct)
    {
        var credential = await credentialRepository.GetByUserIdAsync(userId, ct)
            ?? throw Error(StatusCodes.Status404NotFound, InzhurErrorCodes.NotConnected, "No Inzhur sign-in is in progress.");

        var digits = Digits(code);
        if (digits.Length == 0)
            return new InzhurConnectResult(InzhurConnectResult.InvalidCode);

        var outcome = await browserLogin.VerifyAsync(userId, digits, ct);
        return await ApplyAsync(credential, outcome, ct);
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
            ? new InzhurConnectionStatusResult(InzhurConnectionStatusResult.NotConnected, false, null, null, browserLogin.IsAvailable)
            : new InzhurConnectionStatusResult(
                credential.Status, credential.HasLoginSecrets, credential.LastSyncAt, credential.SessionStartedAt, browserLogin.IsAvailable);
    }

    private async Task<InzhurConnectResult> ApplyAsync(InzhurCredential credential, InzhurLoginOutcome outcome, CancellationToken ct)
    {
        switch (outcome)
        {
            case InzhurLoginOutcome.Authenticated authenticated:
                credential.StartSession(Encrypt(authenticated.Session.Serialize()), Now);
                await credentialRepository.SaveChangesAsync(ct);
                logger.LogInformation("Inzhur connected for user {UserId}", credential.UserId);
                await TryResolveAlertAsync(credential.UserId);
                await TrySyncAsync(credential.UserId, ct);
                return new InzhurConnectResult(InzhurConnectResult.Connected);

            case InzhurLoginOutcome.CodeRequired codeRequired:
                return new InzhurConnectResult(InzhurConnectResult.CodeRequired, CodeExpiresAt: codeRequired.CodeExpiresAt);

            case InzhurLoginOutcome.InvalidCode invalidCode:
                return new InzhurConnectResult(InzhurConnectResult.InvalidCode, AttemptsLeft: invalidCode.AttemptsLeft);

            case InzhurLoginOutcome.Failed failed:
                if (failed.ErrorCode == InzhurErrorCodes.InvalidCredentials)
                {
                    credential.ClearLoginSecrets();
                    await credentialRepository.SaveChangesAsync(ct);
                }

                logger.LogWarning("Inzhur sign-in for user {UserId} ended with {ErrorCode}", credential.UserId, failed.ErrorCode);
                throw Error(StatusFor(failed.ErrorCode), failed.ErrorCode, MessageFor(failed.ErrorCode));

            default:
                throw new InvalidOperationException($"Unhandled Inzhur sign-in outcome {outcome.GetType().Name}.");
        }
    }

    // The first read right after a sign-in, so the holdings show at once; a failure here leaves the connection in
    // place for the daily job.
    private async Task TrySyncAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            await syncService.SyncAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("First Inzhur read after sign-in failed for user {UserId}: {ErrorType}", userId, ex.GetType().Name);
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

    private static int StatusFor(string errorCode) => errorCode switch
    {
        InzhurErrorCodes.LoginUnavailable => StatusCodes.Status503ServiceUnavailable,
        InzhurErrorCodes.LoginLimit => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    private static string MessageFor(string errorCode) => errorCode switch
    {
        InzhurErrorCodes.RecaptchaRejected => "Inzhur's bot check rejected the sign-in.",
        InzhurErrorCodes.InvalidCredentials => "Inzhur did not accept the phone number or password.",
        InzhurErrorCodes.ChallengeExpired => "The SMS code expired. Start the sign-in again.",
        InzhurErrorCodes.TooManyAttempts => "Too many wrong codes. Start the sign-in again.",
        InzhurErrorCodes.LoginUnavailable => "Inzhur sign-in is unavailable right now. Try again later.",
        _ => "Inzhur sign-in failed.",
    };

    private static InzhurConnectException Error(int status, string code, string message) => new(status, code, message);

    // The cabinet sends the phone as digits only (its sign-in form strips everything else); codes likewise.
    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private EncryptedSecret Encrypt(string plaintext)
    {
        var result = encryption.Encrypt(plaintext);
        return new EncryptedSecret(result.Ciphertext, result.Iv, result.AuthTag, result.KeyVersion);
    }

    private string Decrypt(byte[] ciphertext, byte[] iv, byte[] authTag, int keyVersion)
        => encryption.Decrypt(ciphertext, iv, authTag, keyVersion);
}
