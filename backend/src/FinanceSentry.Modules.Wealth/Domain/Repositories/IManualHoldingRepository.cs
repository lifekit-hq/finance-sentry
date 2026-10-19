namespace FinanceSentry.Modules.Wealth.Domain.Repositories;

public interface IManualHoldingRepository
{
    Task<IReadOnlyList<ManualHolding>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The person's holdings for the wealth source, which the snapshot job reads with no person in scope.
    /// Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<ManualHolding>> ListUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The person's holdings including removed ones, for dating the scope changes they caused.
    /// Opts out of the Owner query filter and the removed filter.</summary>
    Task<IReadOnlyList<ManualHolding>> ListIncludingRemovedUnscopedAsync(Guid userId, CancellationToken ct = default);

    Task<ManualHolding?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task AddAsync(ManualHolding holding, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}
