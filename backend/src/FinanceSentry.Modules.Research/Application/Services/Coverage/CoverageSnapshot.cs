namespace FinanceSentry.Modules.Research.Application.Services.Coverage;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Coverage;

/// <summary>
/// One user's coverage inputs as of one instant, read once: who is covered, what is held and watched, the
/// candidates and their latest scores, the theses and the stored verdict readings. The evaluator and the
/// explanation turn it into <see cref="CoverageTickerFacts"/> per ticker. Case-insensitive on tickers throughout.
/// </summary>
public sealed class CoverageSnapshot
{
    public required DateTimeOffset AsOf { get; init; }

    public required IReadOnlyList<CoverageMember> Members { get; init; }

    public required IReadOnlyList<BrokerageHoldingSummary> Holdings { get; init; }

    public required HashSet<string> Held { get; init; }

    public required HashSet<string> Watchlist { get; init; }

    /// <summary>Tickers of theses that are not broken.</summary>
    public required HashSet<string> ThesisTickers { get; init; }

    /// <summary>The earliest dated catalyst per ticker among unbroken theses, inside the lookahead.</summary>
    public required Dictionary<string, DateOnly> ThesisCatalysts { get; init; }

    public required Dictionary<string, List<CoverageCandidateFacts>> Candidates { get; init; }

    /// <summary>The newest candidate score per ticker, over all of the ticker's candidates.</summary>
    public required Dictionary<string, CandidateScore> LatestScores { get; init; }

    /// <summary>The latest dated event per ticker (a rescore, a thesis or candidate lifecycle event).</summary>
    public required Dictionary<string, DateTimeOffset> LastEvents { get; init; }

    /// <summary>Stored verdict readings per ticker, ascending by day.</summary>
    public required Dictionary<string, List<CoverageVerdictDay>> Verdicts { get; init; }

    /// <summary>When the journal last recorded each ticker's removal.</summary>
    public required Dictionary<string, DateTimeOffset> LastRemovals { get; init; }

    /// <summary>The ticker's Active candidate id, when it has one.</summary>
    public required Dictionary<string, Guid> ActiveCandidateIds { get; init; }

    public bool HasThesis(string ticker) => ThesisTickers.Contains(ticker);

    /// <summary>The facts the rules read for <paramref name="ticker"/>.</summary>
    public CoverageTickerFacts Facts(
        string ticker, DateOnly? nextCatalyst, bool catalystKnown, decimal? correlation, int correlationPenalty)
    {
        LatestScores.TryGetValue(ticker, out var score);
        LastEvents.TryGetValue(ticker, out var lastEvent);
        var catalyst = nextCatalyst;
        if (ThesisCatalysts.TryGetValue(ticker, out var thesisCatalyst) && (catalyst is null || thesisCatalyst < catalyst))
        {
            catalyst = thesisCatalyst;
        }

        return new CoverageTickerFacts(
            ticker,
            Held.Contains(ticker),
            Watchlist.Contains(ticker),
            ThesisTickers.Contains(ticker),
            Candidates.GetValueOrDefault(ticker) ?? [],
            Verdicts.GetValueOrDefault(ticker) ?? [],
            lastEvent == default ? null : lastEvent,
            catalyst,
            // A thesis catalyst is a known fact even when the earnings lookup failed.
            catalystKnown || thesisCatalyst != default,
            RankBase(score),
            correlation,
            correlationPenalty)
        {
            LastRemovedAt = LastRemovals.TryGetValue(ticker, out var removedAt) ? removedAt : null,
        };
    }

    private static int? RankBase(CandidateScore? score)
    {
        if (score is null)
        {
            return null;
        }

        var parts = new[] { score.StructureScore, score.FundamentalsScore }.Where(p => p.HasValue).Select(p => p!.Value).ToList();
        return parts.Count == 0 ? null : (int)Math.Round(parts.Average(), MidpointRounding.AwayFromZero);
    }
}
