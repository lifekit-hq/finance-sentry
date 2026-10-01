namespace FinanceSentry.Modules.Risk.Domain.Repositories;

public interface IHoldingSnapshotRepository
{
    Task AddRangeAsync(IReadOnlyList<HoldingSnapshot> snapshots, CancellationToken ct = default);

    Task<IReadOnlyList<HoldingSnapshot>> ListSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>The user's snapshot history for the daily check and the drawdown measure it shares with the alert-side reader, which can run with no person in scope. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<HoldingSnapshot>> ListSinceUnscopedAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default);

    Task<IReadOnlyList<HoldingSnapshot>> ListForSymbolAsync(
        Guid userId, string symbol, string sleeve, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetUserIdsAsync(CancellationToken ct = default);
}
