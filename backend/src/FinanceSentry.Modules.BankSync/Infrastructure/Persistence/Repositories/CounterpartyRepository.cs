namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;

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
        return await _context.Counterparties
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
