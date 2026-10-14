namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// For a closing account: deletes the stored IBKR, IBKR Flex and Inzhur credentials, which stops their syncs (they
/// run per active credential). Holdings, trades and cash stay until the purge. None of these providers offers a revoke
/// API for them: the person revokes the IBKR OAuth consumer or Flex token and ends the Inzhur session themselves.
/// </summary>
internal sealed class BrokerageSyncCredentialRevoker(BrokerageSyncDbContext db) : IUserCredentialRevoker
{
    public string Module => "BrokerageSync";

    public async Task<CredentialRevocation> RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        var removed = await db.IBKRCredentials.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        removed += await db.IBKRFlexCredentials.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        removed += await db.InzhurCredentials.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        return CredentialRevocation.Of(removed);
    }
}
