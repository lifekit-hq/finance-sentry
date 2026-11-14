namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// The one peer-set rule shared by <c>get_valuation_snapshot</c> and <c>get_comps_valuation</c>: the
/// requested tickers or Yahoo's similar symbols, upper-cased, deduplicated, without the ticker itself and
/// capped at <see cref="MaxPeers"/>; named for the sector unless the caller supplied the set.
/// </summary>
public static class ValuationPeerSymbols
{
    public const int MaxPeers = 6;

    public static List<string> Normalize(IEnumerable<string> symbols, string ticker)
        => symbols
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0 && s != ticker)
            .Distinct()
            .Take(MaxPeers)
            .ToList();

    public static string SetName(bool isCustom, string? sector)
        => isCustom
            ? "custom"
            : string.IsNullOrWhiteSpace(sector) ? "default" : $"sector:{sector} (default)";
}
