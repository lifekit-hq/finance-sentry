namespace FinanceSentry.Modules.Research.Domain.Repositories;

using FinanceSentry.Modules.Research.Domain;

public interface IAssetPriceTargetRepository
{
    Task<AssetPriceTarget?> GetAsync(Guid userId, string symbol, CancellationToken ct = default);

    /// <summary>Inserts or overwrites the single target for (user, symbol).</summary>
    Task UpsertAsync(AssetPriceTarget target, CancellationToken ct = default);

    /// <summary>Removes the target for (user, symbol); false when there was none.</summary>
    Task<bool> DeleteAsync(Guid userId, string symbol, CancellationToken ct = default);
}
