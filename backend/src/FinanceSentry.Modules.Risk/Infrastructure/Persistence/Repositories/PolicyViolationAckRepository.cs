namespace FinanceSentry.Modules.Risk.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under the Owner filter. The daily check and the alert-side reader run with no person in scope, so
// they call <see cref="ListActiveUnscopedAsync"/>, which opts out explicitly and keeps its own UserId predicate.
public sealed class PolicyViolationAckRepository(RiskDbContext db) : IPolicyViolationAckRepository
{
    public async Task<IReadOnlyList<PolicyViolationAck>> ListActiveAsync(Guid userId, CancellationToken ct = default)
        => await db.PolicyViolationAcks
            .Where(a => a.UserId == userId && a.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PolicyViolationAck>> ListActiveUnscopedAsync(Guid userId, CancellationToken ct = default)
        => await db.PolicyViolationAcks.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(a => a.UserId == userId && a.IsActive)
            .ToListAsync(ct);

    public Task<PolicyViolationAck?> FindActiveAsync(
        Guid userId, string ruleKey, string subject, CancellationToken ct = default)
        => db.PolicyViolationAcks
            .Where(a => a.UserId == userId && a.RuleKey == ruleKey && a.Subject == subject && a.IsActive)
            .SingleOrDefaultAsync(ct);

    public async Task AddAsync(PolicyViolationAck ack, CancellationToken ct = default)
    {
        db.PolicyViolationAcks.Add(ack);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var ack = await db.PolicyViolationAcks.FindAsync([id], ct);
        if (ack is null)
        {
            return;
        }

        ack.IsActive = false;
        await db.SaveChangesAsync(ct);
    }
}
