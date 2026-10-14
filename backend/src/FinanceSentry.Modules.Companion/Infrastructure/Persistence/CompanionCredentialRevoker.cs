namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// For a closing account: deletes the person's web-push subscriptions (their deliveries go with them by cascade), so
/// no notification reaches a device after the account is closed. The rest of the companion data stays until the purge.
/// </summary>
internal sealed class CompanionCredentialRevoker(CompanionDbContext db) : IUserCredentialRevoker
{
    public string Module => "Companion";

    public async Task<CredentialRevocation> RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        return CredentialRevocation.Of(
            await db.PushSubscriptions.IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync(ct));
    }
}
