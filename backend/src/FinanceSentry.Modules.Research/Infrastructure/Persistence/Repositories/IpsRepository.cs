namespace FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under the Owner filter. Jobs, cross-module readers and the handlers they share run with no person in
// scope, so they call the …Unscoped… methods, which opt out explicitly and keep their own UserId predicate (or are
// an explicit all-users sweep).
public class IpsRepository(ResearchDbContext db) : IIpsRepository
{
    public Task<InvestmentPolicyStatement?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
        => db.PolicyStatements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.IsCurrent, ct);

    public Task<InvestmentPolicyStatement?> GetCurrentUnscopedAsync(Guid userId, CancellationToken ct = default)
        => db.PolicyStatements.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(x => x.UserId == userId && x.IsCurrent, ct);

    public async Task<IReadOnlyList<InvestmentPolicyStatement>> ListVersionsAsync(Guid userId, CancellationToken ct = default)
        => await db.PolicyStatements.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Version)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InvestmentPolicyStatement>> ListVersionsUnscopedAsync(
        Guid userId, CancellationToken ct = default)
        => await db.PolicyStatements.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Version)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetUserIdsWithCurrentIpsUnscopedAsync(CancellationToken ct = default)
        => await db.PolicyStatements.AsNoTracking()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(x => x.IsCurrent)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<int> GetMaxVersionAsync(Guid userId, CancellationToken ct = default)
        => await db.PolicyStatements
            .Where(x => x.UserId == userId)
            .Select(x => (int?)x.Version)
            .MaxAsync(ct) ?? 0;

    public async Task AddVersionAsync(InvestmentPolicyStatement ips, CancellationToken ct = default)
    {
        // Demote the prior current version so exactly one row is IsCurrent per user.
        await db.PolicyStatements
            .Where(x => x.UserId == ips.UserId && x.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsCurrent, false), ct);

        db.PolicyStatements.Add(ips);
        await db.SaveChangesAsync(ct);
    }
}
