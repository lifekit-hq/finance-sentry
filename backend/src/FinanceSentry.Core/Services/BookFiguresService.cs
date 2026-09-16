namespace FinanceSentry.Core.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using Microsoft.Extensions.Logging;

/// <summary>
/// Single canonical implementation of <see cref="IBookFiguresService"/>.
/// Reads all three sources once, splits idle brokerage cash (instrument type "CASH") and fiat
/// held on crypto venues from invested positions, and returns a consistent book snapshot.
/// </summary>
public sealed class BookFiguresService(
    IBankingAccountsReader bankingReader,
    IBrokerageHoldingsReader brokerageReader,
    ICryptoHoldingsReader cryptoReader,
    ILogger<BookFiguresService> logger) : IBookFiguresService
{
    public async Task<BookFigures> ReadAsync(Guid userId, CancellationToken ct = default)
    {
        var positions = new List<BookFigurePosition>();
        var staleSources = new List<string>();
        var bankingCashUsd = 0m;
        var brokerageCashUsd = 0m;
        var venueCashUsd = 0m;

        try
        {
            foreach (var h in await cryptoReader.GetHoldingsAsync(userId, ct))
            {
                var qty = h.FreeQuantity + h.LockedQuantity;
                if (qty == 0m) continue;
                if (h.IsVenueFiat)
                {
                    venueCashUsd += h.UsdValue;
                    continue;
                }

                positions.Add(new BookFigurePosition(h.Asset, AssetClassNormalizer.Crypto, qty, h.CostBasisUsd, h.UsdValue, h.Provider));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Crypto holdings unavailable for user {UserId}; contributing empty.", userId);
            staleSources.Add("crypto");
        }

        try
        {
            foreach (var h in await brokerageReader.GetHoldingsAsync(userId, ct))
            {
                if (h.Quantity == 0m) continue;
                var assetClass = AssetClassNormalizer.Normalize(h.InstrumentType);
                if (assetClass == AssetClassNormalizer.Cash)
                    brokerageCashUsd += h.UsdValue;
                else
                    positions.Add(new BookFigurePosition(h.Symbol, assetClass, h.Quantity, h.CostBasisUsd, h.UsdValue, h.Provider));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Brokerage holdings unavailable for user {UserId}; contributing empty.", userId);
            staleSources.Add("brokerage");
        }

        try
        {
            var accounts = await bankingReader.GetAccountSummariesAsync(userId, ct);
            bankingCashUsd = accounts.Sum(a => AccountBalanceMath.SignedForNetTotal(a.AccountType, a.BalanceUsd ?? 0m));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Banking accounts unavailable for user {UserId}; contributing zero cash.", userId);
            staleSources.Add("banking");
        }

        var cashUsd = bankingCashUsd + brokerageCashUsd + venueCashUsd;
        var investedValueUsd = positions.Sum(p => p.UsdValue);

        return new BookFigures(
            cashUsd,
            bankingCashUsd,
            brokerageCashUsd,
            investedValueUsd,
            investedValueUsd + cashUsd,
            positions,
            staleSources.Count > 0,
            staleSources,
            venueCashUsd);
    }
}
