using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.CryptoSync.Domain;

namespace FinanceSentry.Modules.CryptoSync.Application.Services;

/// <summary>
/// A holding's USD value as the module's readers hand it out. Crypto is valued by the adapter at
/// sync time from venue prices. Venue fiat is a native amount, converted here — the reader
/// boundary, where its currency is in scope — with the current FX rate, as bank balances are.
/// </summary>
public static class CryptoHoldingValuation
{
    public static decimal UsdValue(CryptoHolding holding) => Convert(holding).Amount;

    /// <summary>
    /// <see cref="UsdValue"/> with how it was converted: a crypto position is valued in USD at sync time
    /// (<see cref="FxStatus.Exact"/>), venue fiat carries the status of its own conversion.
    /// </summary>
    public static ConvertResult Convert(CryptoHolding holding)
    {
        if (!holding.IsFiat)
            return new ConvertResult(holding.UsdValue, FxStatus.Exact);

        var converted = CurrencyConverter.Convert(holding.FreeQuantity + holding.LockedQuantity, holding.Asset);
        return converted with { Amount = Math.Round(converted.Amount, 4) };
    }
}
