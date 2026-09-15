namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// Stage 1 of the opportunity funnel (#558): pure, deterministic composition of a bounded shortlist
/// out of the index constituent list, using only signals that cost no per-ticker bar math.
///
/// The stage is itself cheapest-first. <see cref="SelectGradingSlate"/> ranks the whole index on two
/// free-or-batched signals — the day's percent change (coarse momentum) and the street's recent
/// favourable actions — and hands back only as many names as a run may afford to grade.
/// <see cref="Compose"/> then re-ranks that slate on the EDGAR fundamentals grade and caps it at the
/// shortlist size. A name EDGAR cannot grade keeps its surface standing and sorts below every graded
/// name rather than having its missing half defaulted to a faked number (the rule
/// <see cref="ScanNominationRules.RankByQualityMomentum"/> already follows in stage 2).
/// </summary>
public static class ScanShortlistRules
{
    /// <summary>
    /// The index ranked on surface signals alone, truncated to the run's EDGAR grading budget. Only
    /// names carrying at least one signal are ranked — a constituent with neither a quote nor a
    /// street action is invisible to stage 1 this run rather than filling a slate slot at score zero.
    /// </summary>
    public static IReadOnlyList<ScanShortlistEntry> SelectGradingSlate(
        IReadOnlyList<string> constituents,
        IReadOnlyDictionary<string, decimal> percentChangeByTicker,
        IReadOnlyDictionary<string, int> favourableActionsByTicker,
        OpportunityOptions options)
    {
        var streetWeight = Math.Clamp(options.ScanShortlistStreetWeight, 0m, 1m);
        var actionPoints = Math.Max(0, options.ScanShortlistStreetActionPoints);
        var momentum = PercentileRanks.Of(Quoted(constituents, percentChangeByTicker));

        var slate = new List<ScanShortlistEntry>();
        foreach (var ticker in Normalize(constituents))
        {
            var percentile = momentum.TryGetValue(ticker, out var p) ? p : (decimal?)null;
            var actions = favourableActionsByTicker.TryGetValue(ticker, out var a) ? Math.Max(0, a) : 0;
            if (percentile is null && actions == 0)
            {
                continue;
            }

            var street = Math.Min(100m, actions * actionPoints);
            var surface = Math.Round(((percentile ?? 0m) * (1m - streetWeight)) + (street * streetWeight), 2);
            slate.Add(new ScanShortlistEntry(ticker, percentile, actions, surface));
        }

        return [.. slate
            .OrderByDescending(e => e.SurfaceScore)
            .ThenBy(e => e.Ticker, StringComparer.Ordinal)
            .Take(Math.Max(0, options.ScanShortlistGradeBudget))];
    }

    /// <summary>
    /// The graded slate re-ranked on quality x surface momentum and capped at
    /// <see cref="OpportunityOptions.ScanShortlistSize"/> — the names stage 2 pays bar math for.
    /// </summary>
    public static IReadOnlyList<ScanShortlistEntry> Compose(
        IReadOnlyList<ScanShortlistEntry> slate,
        IReadOnlyDictionary<string, int?> qualityByTicker,
        OpportunityOptions options)
    {
        var qualityWeight = Math.Clamp(options.ScanShortlistQualityWeight, 0m, 1m);

        return [.. slate
            .Select(Score)
            .OrderByDescending(e => e.ShortlistScore is not null)
            .ThenByDescending(e => e.ShortlistScore ?? e.SurfaceScore)
            .ThenBy(e => e.Ticker, StringComparer.Ordinal)
            .Take(Math.Max(0, options.ScanShortlistSize))];

        ScanShortlistEntry Score(ScanShortlistEntry entry)
        {
            var quality = qualityByTicker.TryGetValue(entry.Ticker, out var grade) ? grade : null;
            decimal? shortlistScore = quality is { } graded
                ? Math.Round((graded * qualityWeight) + (entry.SurfaceScore * (1m - qualityWeight)), 2)
                : null;

            return entry with { QualityScore = quality, ShortlistScore = shortlistScore };
        }
    }

    private static Dictionary<string, decimal> Quoted(
        IReadOnlyList<string> constituents, IReadOnlyDictionary<string, decimal> percentChangeByTicker)
    {
        var quoted = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticker in Normalize(constituents))
        {
            if (percentChangeByTicker.TryGetValue(ticker, out var change))
            {
                quoted[ticker] = change;
            }
        }

        return quoted;
    }

    private static IEnumerable<string> Normalize(IReadOnlyList<string> constituents)
        => constituents
            .Select(t => t.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal);
}

/// <summary>
/// One stage-1 candidate and the trace of how it got there: its coarse momentum percentile, the
/// count of recent favourable street actions, the composed surface score, and — once graded — its
/// EDGAR fundamentals score and the combined shortlist score. Null quality means EDGAR had no answer.
/// </summary>
public sealed record ScanShortlistEntry(
    string Ticker,
    decimal? MomentumPercentile,
    int FavourableActions,
    decimal SurfaceScore,
    int? QualityScore = null,
    decimal? ShortlistScore = null);
