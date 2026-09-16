using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

public sealed class CryptoHoldingsReader : ICryptoHoldingsReader
{
    private readonly ICryptoHoldingRepository _holdingRepository;

    public CryptoHoldingsReader(ICryptoHoldingRepository holdingRepository)
    {
        _holdingRepository = holdingRepository;
    }

    public async Task<IReadOnlyList<CryptoHoldingSummary>> GetHoldingsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var holdings = await _holdingRepository.GetByUserIdAsync(userId, ct);

        return holdings
            .Select(h => new CryptoHoldingSummary(
                h.Asset,
                h.FreeQuantity,
                h.LockedQuantity,
                CryptoHoldingValuation.UsdValue(h),
                h.SyncedAt,
                h.Provider,
                h.IsFiat ? null : h.CostBasisUsd,
                h.IsFiat))
            .ToList();
    }
}
