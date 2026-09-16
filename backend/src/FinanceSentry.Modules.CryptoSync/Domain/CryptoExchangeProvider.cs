namespace FinanceSentry.Modules.CryptoSync.Domain;

/// <summary>
/// The crypto venues this module syncs. The slug is what <see cref="CryptoHolding.Provider"/>,
/// <see cref="ExchangeCredential.Provider"/> and <c>ICryptoExchangeAdapter.ExchangeName</c> carry,
/// and what crosses the module boundary on <c>CryptoHoldingSummary.Provider</c>.
/// </summary>
public static class CryptoExchangeProvider
{
    public const string Binance = "binance";
    public const string RevolutX = "revolut_x";

    public static string DisplayName(string provider) => provider switch
    {
        Binance => "Binance",
        RevolutX => "Revolut X",
        _ => provider,
    };
}
