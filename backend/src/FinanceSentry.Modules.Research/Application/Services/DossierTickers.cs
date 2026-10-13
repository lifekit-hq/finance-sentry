namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Domain;

/// <summary>Maps a book symbol to the ticker the free chart source quotes it under.</summary>
public static class DossierTickers
{
    /// <summary>The S&amp;P 500 baseline: the SPY ETF, which the chart source quotes like any equity.</summary>
    public const string Benchmark = "SPY";

    public const string BenchmarkLabel = "S&P 500 (SPY)";

    /// <summary>Crypto is quoted against the dollar (<c>BTC-USD</c>); everything else under its own symbol.</summary>
    public static string ToChartTicker(string symbol, string? assetClass)
        => string.Equals(assetClass, AssetClassNormalizer.Crypto, StringComparison.OrdinalIgnoreCase)
            ? $"{symbol.Trim().ToUpperInvariant()}-USD"
            : symbol.Trim().ToUpperInvariant();

    public static bool IsCrypto(string? assetClass)
        => string.Equals(assetClass, AssetClassNormalizer.Crypto, StringComparison.OrdinalIgnoreCase);
}
