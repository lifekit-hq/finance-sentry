namespace FinanceSentry.Core.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;

/// <summary>
/// The banking accounts as a <see cref="IWealthSource"/>: signed balance per account type (a liability reads
/// negative). An account with no reported balance converted nothing, so its status says nothing.
/// </summary>
public sealed class BankingWealthSource(IBankingAccountsReader reader) : IWealthSource
{
    public string SourceId => "banking";

    public EconomyKind Kind => EconomyKind.Bank;

    public async Task<WealthSourceReading> ReadAsync(Guid userId, CancellationToken ct = default)
    {
        var accounts = await reader.GetAccountSummariesAsync(userId, ct);
        var cashUsd = accounts.Sum(a => AccountBalanceMath.SignedForNetTotal(a.AccountType, a.BalanceUsd ?? 0m));
        var fxStatus = FxStatus.Exact;
        foreach (var a in accounts.Where(a => a.BalanceUsd.HasValue))
            fxStatus = fxStatus.Worst(a.FxStatus);

        return new WealthSourceReading(cashUsd, [], fxStatus);
    }
}

/// <summary>
/// The brokerage holdings as a <see cref="IWealthSource"/>: idle cash (instrument type "CASH") is cash, the
/// rest are invested positions. Zero-quantity rows are left out, status included.
/// </summary>
public sealed class BrokerageWealthSource(IBrokerageHoldingsReader reader) : IWealthSource
{
    public string SourceId => "brokerage";

    public EconomyKind Kind => EconomyKind.Brokerage;

    public async Task<WealthSourceReading> ReadAsync(Guid userId, CancellationToken ct = default)
    {
        var positions = new List<BookFigurePosition>();
        var cashUsd = 0m;
        var fxStatus = FxStatus.Exact;

        foreach (var h in await reader.GetHoldingsAsync(userId, ct))
        {
            if (h.Quantity == 0m) continue;
            fxStatus = fxStatus.Worst(h.FxStatus);
            var assetClass = AssetClassNormalizer.Normalize(h.InstrumentType);
            if (assetClass == AssetClassNormalizer.Cash)
                cashUsd += h.UsdValue;
            else
                positions.Add(new BookFigurePosition(h.Symbol, assetClass, h.Quantity, h.CostBasisUsd, h.UsdValue, h.Provider));
        }

        return new WealthSourceReading(cashUsd, positions, fxStatus);
    }
}

/// <summary>
/// The crypto venues as a <see cref="IWealthSource"/>: fiat held on a venue is cash, every other balance a
/// crypto position. Zero-quantity rows are left out, status included.
/// </summary>
public sealed class CryptoWealthSource(ICryptoHoldingsReader reader) : IWealthSource
{
    public string SourceId => "crypto";

    public EconomyKind Kind => EconomyKind.CryptoVenue;

    public async Task<WealthSourceReading> ReadAsync(Guid userId, CancellationToken ct = default)
    {
        var positions = new List<BookFigurePosition>();
        var cashUsd = 0m;
        var fxStatus = FxStatus.Exact;

        foreach (var h in await reader.GetHoldingsAsync(userId, ct))
        {
            var qty = h.FreeQuantity + h.LockedQuantity;
            if (qty == 0m) continue;
            fxStatus = fxStatus.Worst(h.FxStatus);
            if (h.IsVenueFiat)
            {
                cashUsd += h.UsdValue;
                continue;
            }

            positions.Add(new BookFigurePosition(h.Asset, AssetClassNormalizer.Crypto, qty, h.CostBasisUsd, h.UsdValue, h.Provider));
        }

        return new WealthSourceReading(cashUsd, positions, fxStatus);
    }
}
