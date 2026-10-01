namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="ICounterpartyRepository"/>.
/// </summary>
public class CounterpartyRepository(BankSyncDbContext context) : ICounterpartyRepository
{
    private readonly BankSyncDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Counterparty>> GetForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        // Deterministic order (FR-009): classification is first-match-wins, so an unordered
        // read would let the database decide which counterparty claims an ambiguous match.
        // Opts out of the Owner filter: the shared system counterparties (UserId == Guid.Empty) belong to
        // nobody in particular, and the family-clearing job reads this with no person in scope. The
        // explicit predicate below keeps the read scoped to the user plus those shared rows.
        return await _context.Counterparties.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Include(c => c.Rules)
            .Where(c => c.UserId == userId || c.UserId == Guid.Empty)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Counterparty?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Counterparties.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Counterparty> UpdateAsync(Counterparty counterparty, CancellationToken cancellationToken = default)
    {
        _context.Counterparties.Update(counterparty);
        await _context.SaveChangesAsync(cancellationToken);
        return counterparty;
    }
}
