namespace FinanceSentry.Modules.Risk.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under the Owner filter. The daily check, the portfolio scan and the cross-module readers run with
// no person in scope, so they call the <c>…Unscoped…</c> methods, which opt out explicitly and keep their own
// UserId predicate (or an explicit all-users sweep).
public sealed class RiskRuleSetRepository(RiskDbContext db) : IRiskRuleSetRepository
{
    public Task<RiskRuleSet?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
        => db.RiskRuleSets
            .Where(r => r.UserId == userId && r.IsCurrent)
            .SingleOrDefaultAsync(ct);

    public Task<RiskRuleSet?> GetCurrentUnscopedAsync(Guid userId, CancellationToken ct = default)
        => db.RiskRuleSets.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(r => r.UserId == userId && r.IsCurrent)
            .SingleOrDefaultAsync(ct);

    public async Task<RiskRuleSet> SaveNewVersionAsync(RiskRuleSet ruleSet, CancellationToken ct = default)
    {
        var current = await db.RiskRuleSets
            .Where(r => r.UserId == ruleSet.UserId && r.IsCurrent)
            .SingleOrDefaultAsync(ct);

        if (current is not null)
        {
            current.IsCurrent = false;
        }

        ruleSet.Version = (current?.Version ?? 0) + 1;
        ruleSet.IsCurrent = true;

        db.RiskRuleSets.Add(ruleSet);
        await db.SaveChangesAsync(ct);
        return ruleSet;
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsWithRuleSetsUnscopedAsync(CancellationToken ct = default)
        => await db.RiskRuleSets.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(r => r.IsCurrent)
            .Select(r => r.UserId)
            .Distinct()
            .ToListAsync(ct);
}
