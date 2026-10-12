using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

public enum InzhurKeepAliveOutcome
{
    /// <summary>The session was refreshed and the rotated jar stored.</summary>
    Refreshed,

    /// <summary>No active connection with a session to use; nothing was called.</summary>
    Skipped,
}

public interface IInzhurKeepAliveService
{
    /// <summary>
    /// One refresh of the user's cabinet session, stored; nothing is read. A refresh is not a sign-in: it is the same
    /// call the cabinet makes for its own tab, it sends no SMS and it spends nothing of any sign-in allowance. Throws
    /// <see cref="InzhurApiException"/> when Inzhur fails: a dead session also flips the connection to
    /// <see cref="InzhurConnectionStatus.ReauthRequired"/> first, while a busy or unreachable Inzhur leaves the
    /// connection exactly as it was for the next run.
    /// </summary>
    Task<InzhurKeepAliveOutcome> KeepAliveAsync(Guid userId, CancellationToken ct = default);
}

public sealed class InzhurKeepAliveService(
    IInzhurCredentialRepository credentialRepository,
    IInzhurApiClient api,
    IInzhurSessionGate gate,
    ICredentialEncryptionService encryption,
    TimeProvider clock,
    ILogger<InzhurKeepAliveService> logger) : IInzhurKeepAliveService
{
    public async Task<InzhurKeepAliveOutcome> KeepAliveAsync(Guid userId, CancellationToken ct = default)
    {
        // Read the jar only once the turn is ours: whoever held it before may have rotated the refresh token.
        using var turn = await gate.EnterAsync(userId, ct);

        var credential = await credentialRepository.GetFreshByUserIdUnscopedAsync(userId, ct);
        if (credential is null || credential.Status != InzhurConnectionStatus.Active || !credential.HasSession)
            return InzhurKeepAliveOutcome.Skipped;

        try
        {
            var session = InzhurSession.Deserialize(encryption.Decrypt(
                credential.EncryptedSession, credential.SessionIv, credential.SessionAuthTag, credential.SessionKeyVersion));

            var refreshed = await api.RefreshAsync(session, ct);
            var idle = IdleFor(credential);
            credential.RefreshSession(Encrypt(refreshed.Serialize()), Now);
            await credentialRepository.SaveChangesAsync(ct);

            logger.LogInformation(
                "Inzhur session for user {UserId} kept alive after {IdleFor}; session started {SessionStartedAt:o}",
                userId, idle, credential.SessionStartedAt);
            return InzhurKeepAliveOutcome.Refreshed;
        }
        catch (InzhurApiException ex) when (ex.Kind == InzhurFailureKind.ReauthRequired)
        {
            // How long this session lived and how long it sat unrefreshed is what the lifetime floor is tuned from.
            logger.LogWarning(
                "Inzhur session for user {UserId} ended during keep-alive: started {SessionStartedAt:o}, last refreshed {SessionRefreshedAt:o} ({IdleFor} ago); reconnect needed",
                userId, credential.SessionStartedAt, credential.SessionRefreshedAt, IdleFor(credential));
            credential.MarkReauthRequired(ex.Message);
            await credentialRepository.SaveChangesAsync(ct);
            throw;
        }
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private TimeSpan? IdleFor(InzhurCredential credential)
        => credential.SessionRefreshedAt is { } last ? Now - last : null;

    private EncryptedSecret Encrypt(string plaintext)
    {
        var result = encryption.Encrypt(plaintext);
        return new EncryptedSecret(result.Ciphertext, result.Iv, result.AuthTag, result.KeyVersion);
    }
}
