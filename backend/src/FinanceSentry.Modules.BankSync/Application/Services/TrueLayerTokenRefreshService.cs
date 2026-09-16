namespace FinanceSentry.Modules.BankSync.Application.Services;

using System.Collections.Concurrent;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;

/// <summary>
/// Exchanges a TrueLayer connection's rotating refresh_token for a fresh access_token.
/// </summary>
public interface ITrueLayerTokenRefreshService
{
    Task<string> AcquireAccessTokenAsync(Guid connectionId, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// Serializes the refresh-token exchange per TrueLayer connection, shared by every caller in the
/// process (per-account scheduled sync and the account-discovery pass both refresh against the
/// same connection on the same cron). Without this gate two callers could refresh the shared,
/// rotating refresh_token concurrently — one wins, the other gets invalid_grant and the rotated
/// token is lost, bricking the connection. The lock table is a static field (not instance state)
/// so it stays shared across every Scoped instance of this service the container creates.
/// </remarks>
public class TrueLayerTokenRefreshService(
    ITrueLayerConnectionRepository connections,
    ICredentialEncryptionService encryption,
    ITrueLayerClient client) : ITrueLayerTokenRefreshService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> RefreshLocks = new();

    public async Task<string> AcquireAccessTokenAsync(Guid connectionId, CancellationToken ct = default)
    {
        var gate = RefreshLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var connection = await connections.GetByIdAsync(connectionId, ct)
                ?? throw new InvalidOperationException($"TrueLayer connection {connectionId} not found.");

            var refreshToken = encryption.Decrypt(
                connection.EncryptedRefreshToken, connection.Iv, connection.AuthTag, connection.KeyVersion);

            var tokenSet = await client.RefreshAccessTokenAsync(refreshToken, ct);

            // Persist the rotated refresh_token now — not after the caller's own work — so nothing
            // downstream can lose it.
            if (!string.IsNullOrEmpty(tokenSet.RefreshToken) && tokenSet.RefreshToken != refreshToken)
            {
                var encrypted = encryption.Encrypt(tokenSet.RefreshToken);
                connection.SetRefreshToken(encrypted.Ciphertext, encrypted.Iv, encrypted.AuthTag, encrypted.KeyVersion);
                await connections.UpdateAsync(connection, ct);
            }

            return tokenSet.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }
}
