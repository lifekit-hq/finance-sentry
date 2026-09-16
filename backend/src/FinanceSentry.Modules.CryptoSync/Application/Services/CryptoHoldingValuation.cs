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
    public static decimal UsdValue(CryptoHolding holding) =>
        holding.IsFiat
            ? Math.Round(CurrencyConverter.ToUsd(holding.FreeQuantity + holding.LockedQuantity, holding.Asset), 4)
            : holding.UsdValue;
}
