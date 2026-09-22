namespace FinanceSentry.Modules.Budgets.Domain.Repositories;

public interface IBudgetRepository
{
    /// <summary>All budgets across all users, for the daily budget-breach hygiene sentinel. Callers
    /// group by <c>UserId</c> themselves so the per-user month-to-date spend read stays batched.</summary>
    Task<IReadOnlyList<Budget>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Budget>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Budget?> FindByUserAndCategoryAsync(Guid userId, string category, CancellationToken ct = default);
    Task<Budget> CreateAsync(Budget budget, CancellationToken ct = default);
    Task UpdateAsync(Budget budget, CancellationToken ct = default);
    Task DeleteAsync(Budget budget, CancellationToken ct = default);
}
