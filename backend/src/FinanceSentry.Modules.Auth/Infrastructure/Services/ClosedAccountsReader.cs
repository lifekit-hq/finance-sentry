using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

internal sealed class ClosedAccountsReader(AuthDbContext db) : IClosedAccountsReader
{
    public async Task<IReadOnlySet<Guid>> GetClosedUserIdsAsync(CancellationToken ct = default)
        => await db.AccountClosures
            .Where(c => c.ReopenedAt == null && c.PurgedAt == null)
            .Select(c => c.UserId)
            .ToHashSetAsync(ct);
}
