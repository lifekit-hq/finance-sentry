namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// For a closing account: withdraws each TrueLayer connection at TrueLayer (best effort), deletes the stored Monobank
/// tokens and TrueLayer connections, and removes the accounts' recurring sync jobs. The accounts and their history stay
/// (the foreign keys to a credential are <c>ON DELETE SET NULL</c>) until the purge. Monobank has no revoke API: the
/// person revokes their personal token at api.monobank.ua.
/// </summary>
internal sealed class BankSyncCredentialRevoker(
    BankSyncDbContext db,
    ITrueLayerClient trueLayer,
    ICredentialEncryptionService encryption,
    IRecurringJobManager recurringJobs,
    ILogger<BankSyncCredentialRevoker> logger) : IUserCredentialRevoker
{
    public string Module => "BankSync";

    public async Task<CredentialRevocation> RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        var failures = new List<string>();
        var connections = await db.TrueLayerConnections.IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.EncryptedRefreshToken.Length > 0)
            .ToListAsync(ct);

        foreach (var connection in connections)
        {
            try
            {
                var refreshToken = encryption.Decrypt(
                    connection.EncryptedRefreshToken, connection.Iv, connection.AuthTag, connection.KeyVersion);
                var tokens = await trueLayer.RefreshAccessTokenAsync(refreshToken, ct);
                await trueLayer.RevokeAccessAsync(tokens.AccessToken, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Revoking TrueLayer connection {ConnectionId} at TrueLayer failed", connection.Id);
                failures.Add($"TrueLayer ({connection.ProviderDisplayName}): {(ex as TrueLayerException)?.ErrorCode ?? ex.GetType().Name}");
            }
        }

        var accountIds = await db.BankAccounts.IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync(ct);
        foreach (var accountId in accountIds)
            recurringJobs.RemoveIfExists(SyncAccountJob.IdFor(accountId));

        var removed = await db.TrueLayerConnections.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        removed += await db.MonobankCredentials.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);

        return new CredentialRevocation(removed, failures);
    }
}
