namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// For a closing account: deletes the stored exchange API keys, which stops the exchange syncs (they run per active
/// credential). Holdings and trades stay until the purge. Exchanges have no revoke API for a key: the person deletes
/// it in the exchange's API management page.
/// </summary>
internal sealed class CryptoSyncCredentialRevoker(CryptoSyncDbContext db) : IUserCredentialRevoker
{
    public string Module => "CryptoSync";

    public async Task<CredentialRevocation> RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        return CredentialRevocation.Of(
            await db.ExchangeCredentials.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct));
    }
}
