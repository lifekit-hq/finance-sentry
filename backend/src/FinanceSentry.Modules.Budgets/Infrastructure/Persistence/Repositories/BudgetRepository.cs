namespace FinanceSentry.Modules.Budgets.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

// Reads run under the Owner filter. The budget-breach sentinel runs with no person in scope, so it calls
// <c>GetAllUnscopedAsync</c>, which opts out explicitly for its all-users sweep.
public class BudgetRepository(BudgetsDbContext db) : IBudgetRepository
{
    private readonly BudgetsDbContext _db = db;

    public Task<IReadOnlyList<Budget>> GetAllUnscopedAsync(CancellationToken ct = default)
        => _db.Budgets.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .ToListAsync(ct)
            .ContinueWith<IReadOnlyList<Budget>>(t => t.Result, ct);

    public Task<IReadOnlyList<Budget>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.Budgets.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Category)
            .ToListAsync(ct)
            .ContinueWith<IReadOnlyList<Budget>>(t => t.Result, ct);

    public Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Budgets.FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<Budget?> FindByUserAndCategoryAsync(Guid userId, string category, CancellationToken ct = default)
        => _db.Budgets.FirstOrDefaultAsync(b => b.UserId == userId && b.Category == category, ct);

    public async Task<Budget> CreateAsync(Budget budget, CancellationToken ct = default)
    {
        _db.Budgets.Add(budget);
        await _db.SaveChangesAsync(ct);
        return budget;
    }

    public Task UpdateAsync(Budget budget, CancellationToken ct = default)
    {
        _db.Budgets.Update(budget);
        return _db.SaveChangesAsync(ct);
    }

    public Task DeleteAsync(Budget budget, CancellationToken ct = default)
    {
        _db.Budgets.Remove(budget);
        return _db.SaveChangesAsync(ct);
    }
}
