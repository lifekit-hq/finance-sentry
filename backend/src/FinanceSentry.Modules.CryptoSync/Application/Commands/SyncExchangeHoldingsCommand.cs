using FinanceSentry.Core.Cqrs;
using FinanceSentry.Infrastructure.Encryption;
using FinanceSentry.Modules.CryptoSync.Application.Services;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.CryptoSync.Application.Commands;

public sealed record SyncExchangeHoldingsCommand(Guid UserId, string Provider) : ICommand<SyncExchangeHoldingsResult>;

public sealed record SyncExchangeHoldingsResult(int HoldingsCount, DateTime SyncedAt);

/// <summary>
/// Syncs one venue's holdings for one user. Everything it reads, writes, reconciles or deletes is
/// scoped to <see cref="SyncExchangeHoldingsCommand.Provider"/>, so two venues holding the same
/// asset never overwrite each other (#472).
/// </summary>
public sealed class SyncExchangeHoldingsCommandHandler(
    IExchangeCredentialRepository credentialRepository,
    ICryptoHoldingRepository holdingRepository,
    CryptoExchangeAdapterRegistry adapters,
    ICredentialEncryptionService encryption,
    CostBasisCalculator costBasisCalculator,
    ILogger<SyncExchangeHoldingsCommandHandler> logger)
    : ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult>
{
    private const decimal QuantityTolerance = 0.01m;
    private const decimal MaximumTrustedCostToValueRatio = 20m;

    public async Task<SyncExchangeHoldingsResult> Handle(SyncExchangeHoldingsCommand request, CancellationToken ct)
    {
        var adapter = adapters.Get(request.Provider);
        var credential = await credentialRepository.GetAsync(request.UserId, request.Provider, ct);
        if (credential is not { IsActive: true })
        {
            throw new ExchangeAccountNotFoundException(request.Provider);
        }

        string apiKey;
        string apiSecret;

        try
        {
            apiKey = encryption.Decrypt(
                credential.EncryptedApiKey,
                credential.ApiKeyIv,
                credential.ApiKeyAuthTag,
                credential.KeyVersion);

            apiSecret = encryption.Decrypt(
                credential.EncryptedApiSecret,
                credential.ApiSecretIv,
                credential.ApiSecretAuthTag,
                credential.KeyVersion);
        }
        catch (Exception ex)
        {
            credential.MarkSyncFailed("Failed to decrypt credentials.");
            credentialRepository.Update(credential);
            await credentialRepository.SaveChangesAsync(ct);
            throw new InvalidOperationException(
                $"Failed to decrypt {CryptoExchangeProvider.DisplayName(request.Provider)} credentials.", ex);
        }

        try
        {
            var balances = await adapter.GetHoldingsAsync(apiKey, apiSecret, ct);

            var holdings = balances
                .Select(b => CryptoHolding.Create(
                    request.UserId,
                    request.Provider,
                    b.Asset,
                    b.FreeQuantity,
                    b.LockedQuantity,
                    b.UsdValue))
                .ToList();

            await holdingRepository.UpsertRangeAsync(holdings, ct);
            await holdingRepository.SaveChangesAsync(ct);

            // Reconcile: the adapter only returns assets the user still holds on this venue (dust
            // and zero balances are already dropped), so anything this venue persisted but did not
            // return was sold out — delete it instead of leaving a stale $0 holding.
            var freshAssets = holdings
                .Select(h => h.Asset)
                .ToHashSet(StringComparer.Ordinal);
            var persisted = await holdingRepository.GetByUserAndProviderAsync(request.UserId, request.Provider, ct);
            var stale = persisted
                .Where(h => !freshAssets.Contains(h.Asset))
                .ToList();
            if (stale.Count > 0)
            {
                holdingRepository.RemoveRange(stale);
                await holdingRepository.SaveChangesAsync(ct);
            }

            await UpdateCostBasisAsync(adapter, request, apiKey, apiSecret, ct);

            var syncedAt = DateTime.UtcNow;
            credential.MarkSynced(syncedAt);
            credentialRepository.Update(credential);
            await credentialRepository.SaveChangesAsync(ct);

            return new SyncExchangeHoldingsResult(holdings.Count, syncedAt);
        }
        catch (CryptoExchangeException ex)
        {
            credential.MarkSyncFailed(ex.Message);
            credentialRepository.Update(credential);
            await credentialRepository.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task UpdateCostBasisAsync(
        ICryptoExchangeAdapter adapter,
        SyncExchangeHoldingsCommand request,
        string apiKey,
        string apiSecret,
        CancellationToken ct)
    {
        var persisted = await holdingRepository.GetByUserAndProviderAsync(request.UserId, request.Provider, ct);

        foreach (var holding in persisted)
        {
            CryptoTradePage page;
            try
            {
                page = await adapter.GetTradesAsync(apiKey, apiSecret, holding.Asset, holding.TradeCursor, ct);
            }
            catch (CryptoExchangeException ex)
            {
                logger.LogWarning(ex,
                    "Trade history fetch failed for {Asset} on {Provider} (user {UserId}); cost basis left unchanged.",
                    holding.Asset, request.Provider, request.UserId);
                continue;
            }

            var trades = page.Trades;
            if (trades.Count == 0)
            {
                // Nothing new; the cursor may still have been normalised by the adapter.
                holding.AdvanceTradeCursor(page.NextCursor);
                continue;
            }

            var seed = holding.TradeCount > 0
                ? new CostBasisResult(
                    CostBasisUsd: holding.CostBasisUsd ?? 0m,
                    AverageBuyPriceUsd: holding.AverageBuyPriceUsd ?? 0m,
                    RemainingQuantity: holding.AverageBuyPriceUsd is > 0m && holding.CostBasisUsd is not null
                        ? holding.CostBasisUsd.Value / holding.AverageBuyPriceUsd.Value
                        : 0m,
                    RealizedPnlUsd: holding.RealizedPnlUsd ?? 0m,
                    LastTradeAt: holding.LastTradeAt,
                    TradeCount: holding.TradeCount)
                : null;

            var result = costBasisCalculator.Compute(trades, seed);

            var currentQuantity = holding.FreeQuantity + holding.LockedQuantity;
            var costBasis = TrustCostBasisForCurrentPosition(result, currentQuantity, holding.UsdValue);

            holding.SetCostBasis(
                costBasis,
                costBasis is null ? null : result.AverageBuyPriceUsd,
                result.RealizedPnlUsd,
                result.LastTradeAt,
                result.TradeCount);

            // Advanced only together with the trades it covers, so a fill is never counted twice.
            holding.AdvanceTradeCursor(page.NextCursor);
        }

        await holdingRepository.SaveChangesAsync(ct);
    }

    private static decimal? TrustCostBasisForCurrentPosition(
        CostBasisResult result,
        decimal currentQuantity,
        decimal currentValueUsd)
    {
        if (currentQuantity <= 0m)
        {
            return 0m;
        }

        if (result.AverageBuyPriceUsd <= 0m || result.RemainingQuantity <= 0m)
        {
            return null;
        }

        var quantityDelta = Math.Abs(result.RemainingQuantity - currentQuantity);
        var tolerance = Math.Max(currentQuantity, result.RemainingQuantity) * QuantityTolerance;

        decimal costBasis;
        if (quantityDelta <= tolerance)
        {
            costBasis = result.CostBasisUsd;
        }
        else if (result.RemainingQuantity > currentQuantity)
        {
            costBasis = result.AverageBuyPriceUsd * currentQuantity;
        }
        else
        {
            return null;
        }

        if (currentValueUsd > 0m && costBasis / currentValueUsd > MaximumTrustedCostToValueRatio)
        {
            return null;
        }

        return Math.Round(costBasis, 4);
    }
}
