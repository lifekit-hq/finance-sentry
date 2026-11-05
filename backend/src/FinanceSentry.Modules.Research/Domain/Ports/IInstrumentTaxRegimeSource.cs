namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Cross-module port: the Irish disposal regime the user has classified a symbol under. The concrete adapter
/// lives in FinanceSentry.Integration so Modules.Research never depends on Modules.BrokerageSync directly.
/// </summary>
public interface IInstrumentTaxRegimeSource
{
    /// <summary>The classified regime, or null when the symbol is unclassified, unknown or ambiguous.</summary>
    Task<DisposalRegime?> GetForSymbolAsync(Guid userId, string symbol, CancellationToken ct = default);
}

/// <summary>The Irish regime an instrument is taxed under on disposal; set by the human classification, never inferred.</summary>
public enum DisposalRegime
{
    /// <summary>An ordinary share: capital gains tax.</summary>
    Cgt = 1,

    /// <summary>An offshore fund / ETF in the gross roll-up regime: exit tax with an 8-year deemed disposal.</summary>
    ExitTax = 2,
}
