namespace FinanceSentry.Modules.Research.Domain.Scoring;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// Pure, deterministic FR-008 nomination rules over the active universe (019 US2):
/// (a) top-quartile RS members of top-N rotating sectors, (b) top-decile RS overall,
/// (c) at the 63-day high on above-average volume. ETF lenses and stale snapshots are
/// never nominated. Reason strings are stable so re-nominations dedup instead of
/// accumulating near-duplicates on the candidate.
/// </summary>
public static class ScanNominationRules
{
    /// <summary>Window (bars) whose relative strength drives rules (a) and (b).</summary>
    public const int RsWindowBars = 63;

    public const string TopQuartileRotatingSectorReason = "scan: top-quartile RS in top rotating sector";
    public const string TopDecileRsReason = "scan: top-decile RS";
    public const string BreakoutReason = "scan: 63d-high breakout on above-average volume";
    public const string QualityMomentumReason = "scan: quality fundamentals with universe momentum";

    public static IReadOnlyList<ScanNomination> Evaluate(
        IReadOnlyList<UniverseStructureEntry> universe, OpportunityOptions options)
    {
        var eligible = universe
            .Where(e => !e.IsEtfLens && !e.Snapshot.Stale)
            .ToList();
        if (eligible.Count == 0)
        {
            return [];
        }

        var rsByTicker = eligible
            .Select(e => (e.Ticker, Rs: RsAtWindow(e.Snapshot)))
            .Where(x => x.Rs is not null)
            .ToDictionary(x => x.Ticker, x => x.Rs!.Value, StringComparer.OrdinalIgnoreCase);
        var percentiles = PercentileRanks.Of(rsByTicker);

        var nominations = new List<ScanNomination>();
        foreach (var entry in eligible)
        {
            var reasons = new List<string>();
            var percentile = percentiles.TryGetValue(entry.Ticker, out var p) ? p : (decimal?)null;

            if (percentile >= options.ScanTopQuartileRsPercentile
                && entry.Snapshot.SectorRank is { } rank
                && rank <= options.ScanTopRotatingSectors)
            {
                reasons.Add(TopQuartileRotatingSectorReason);
            }

            if (percentile >= options.ScanTopDecileRsPercentile)
            {
                reasons.Add(TopDecileRsReason);
            }

            if (entry.Snapshot.DistanceFrom63dHigh == 0m
                && entry.Snapshot.VolumeRatio >= options.ScanBreakoutVolumeRatioMin)
            {
                reasons.Add(BreakoutReason);
            }

            if (reasons.Count > 0)
            {
                nominations.Add(new ScanNomination(entry.Ticker, reasons, percentile));
            }
        }

        return nominations
            .OrderByDescending(n => n.RsPercentile ?? -1m)
            .ThenBy(n => n.Ticker, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Re-ranks momentum nominations by a combined quality x momentum score (019 FR-006): the EDGAR
    /// fundamentals grade weighted against the universe RS percentile. A nomination missing either
    /// half — EDGAR cannot grade crypto/ETFs/non-filers, and a breakout can nominate a ticker with no
    /// RS at all — scores no combined value and ranks below every fully-scored name on its RS alone,
    /// rather than being dropped or having the missing half defaulted to a faked number. The same rule
    /// governs the reason tag: only a name that actually scored on both halves is labelled a
    /// quality x momentum pick.
    /// </summary>
    public static IReadOnlyList<ScanCandidateRank> RankByQualityMomentum(
        IReadOnlyList<ScanNomination> momentumRanked,
        IReadOnlyDictionary<string, int?> qualityByTicker,
        OpportunityOptions options)
    {
        var qualityWeight = Math.Clamp(options.ScanQualityWeight, 0m, 1m);

        return momentumRanked
            .Select(Rank)
            .OrderByDescending(r => r.CombinedScore is not null)
            .ThenByDescending(r => r.CombinedScore ?? r.RsPercentile ?? -1m)
            .ThenBy(r => r.Ticker, StringComparer.Ordinal)
            .ToList();

        ScanCandidateRank Rank(ScanNomination nomination)
        {
            var quality = qualityByTicker.TryGetValue(nomination.Ticker, out var grade) ? grade : null;
            decimal? combined = (quality, nomination.RsPercentile) is ({ } graded, { } momentum)
                ? Math.Round((graded * qualityWeight) + (momentum * (1m - qualityWeight)), 2)
                : null;

            var reasons = nomination.Reasons;
            if (combined is not null && quality >= options.ScanQualityLeaderScore)
            {
                reasons = [.. nomination.Reasons, QualityMomentumReason];
            }

            return new ScanCandidateRank(nomination.Ticker, reasons, nomination.RsPercentile, quality, combined);
        }
    }

    private static decimal? RsAtWindow(MarketStructureSnapshot snapshot)
        => snapshot.RsByWindow.TryGetValue(RsWindowBars, out var rs) ? rs : null;
}

public sealed record ScanNomination(string Ticker, IReadOnlyList<string> Reasons, decimal? RsPercentile);

/// <summary>A nomination carrying its EDGAR fundamentals grade and the combined quality x momentum score.</summary>
public sealed record ScanCandidateRank(
    string Ticker,
    IReadOnlyList<string> Reasons,
    decimal? RsPercentile,
    int? QualityScore,
    decimal? CombinedScore);
