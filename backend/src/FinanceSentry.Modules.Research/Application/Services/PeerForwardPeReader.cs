namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// The forward P/Es of a ticker's default peer set (Yahoo's similar names, the same set
/// <c>get_valuation_snapshot</c> defaults to), for the verdict's fallback price lens. A peer whose metrics
/// cannot be read comes back as null, never a guessed number. It is asked only for a name whose own-history P/E
/// premium is missing, so a scorecard with that history costs no extra reads.
/// </summary>
public sealed class PeerForwardPeReader(IValuationDataService valuation)
{
    public const int MaxPeers = 6;

    public async Task<IReadOnlyList<decimal?>> ReadAsync(string ticker, CancellationToken ct)
    {
        var symbols = (await valuation.GetPeerSymbolsAsync(ticker, ct))
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0 && s != ticker)
            .Distinct()
            .Take(MaxPeers)
            .ToList();

        var forwardPes = new List<decimal?>(symbols.Count);
        foreach (var symbol in symbols)
        {
            forwardPes.Add((await valuation.GetCurrentMetricsAsync(symbol, ct))?.ForwardPe);
        }

        return forwardPes;
    }
}
