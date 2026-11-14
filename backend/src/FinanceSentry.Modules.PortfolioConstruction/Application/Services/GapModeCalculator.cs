namespace FinanceSentry.Modules.PortfolioConstruction.Application.Services;

using System.Globalization;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.PortfolioConstruction.API.Responses;

/// <summary>Where a held figure stands against its target (spec 726 US4).</summary>
public static class GapStatus
{
    public const string Missing = "Missing";
    public const string Underweight = "Underweight";
    public const string OnTarget = "OnTarget";
    public const string Overweight = "Overweight";
}

/// <summary>
/// Gap mode's arithmetic (spec 726 US4): the canonical book against the construction the universe screen built.
/// Pure and deterministic: the same book and construction always give the same gap, with no model call. Every
/// percentage is of the whole book (cash included), the same denominator as the construction's weights, and a gap
/// is always target minus current. It reads the construction; it never builds, stores or trades anything (FR-018).
/// </summary>
public static class GapModeCalculator
{
    public const string InformationalLabel =
        "Informational only: what you hold against a rules-built target. It is not a recommendation to buy or sell, "
        + "and nothing here places or prepares a trade.";

    public const string Unclassified = "Unclassified";

    /// <summary>A gap this small (percentage points of the book) reads as on target, not as a shortfall or an excess.</summary>
    public const decimal OnTargetTolerancePct = 0.5m;

    private const decimal Hundred = 100m;
    private const int Decimals = 2;

    public static GapModeResult Compute(UniverseScreenResult universe, BookFigures book, DateTimeOffset generatedAt)
    {
        var total = book.TotalValueUsd;
        var hasBook = total > 0m;
        var notes = new List<string>(universe.Construction.Notes);
        if (book.IsStale)
        {
            notes.Add($"Book figures are stale for {string.Join(", ", book.StaleSources)}: the held side may be incomplete.");
        }

        if (!hasBook)
        {
            notes.Add("The book has no value to size against, so only the target weights are shown and amounts are unavailable. "
                + "With nothing held, every target name is missing: the universe screen is the place to start.");
        }

        if (universe.Construction.Refusal is { } refusal)
        {
            return new GapModeResult(
                universe.Archetype, generatedAt, InformationalLabel, hasBook, total, book.IsStale, book.StaleSources, refusal,
                new GapSummaryDto(refusal, [], [], [], 0), [], [], [], notes);
        }

        var names = NameGaps(universe, book, total, hasBook);
        var sectors = SectorGaps(names, total, hasBook);
        var sleeves = SleeveGaps(universe, book, total, hasBook);

        return new GapModeResult(
            universe.Archetype, generatedAt, InformationalLabel, hasBook, total, book.IsStale, book.StaleSources, null,
            Summarize(names, sleeves), sleeves, sectors, names, notes);
    }

    private static List<NameGapDto> NameGaps(UniverseScreenResult universe, BookFigures book, decimal total, bool hasBook)
    {
        var rows = universe.Rows.ToDictionary(r => r.Symbol.Trim().ToUpperInvariant(), StringComparer.Ordinal);
        var target = rows
            .Where(r => r.Value.SuggestedWeightPct is > 0m)
            .ToDictionary(r => r.Key, r => r.Value.SuggestedWeightPct!.Value, StringComparer.Ordinal);

        var held = book.Positions
            .Where(p => p.UsdValue > 0m && AssetClassNormalizer.Normalize(p.AssetClass) == AssetClassNormalizer.Equities)
            .GroupBy(p => p.Symbol.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.UsdValue), StringComparer.Ordinal);

        var gaps = new List<NameGapDto>();
        foreach (var symbol in target.Keys.Union(held.Keys, StringComparer.Ordinal))
        {
            var currentUsd = held.GetValueOrDefault(symbol);
            var targetPct = target.GetValueOrDefault(symbol);
            var row = rows.GetValueOrDefault(symbol);
            var figures = Figures(currentUsd, targetPct, total, hasBook);
            var outside = targetPct <= 0m;

            gaps.Add(new NameGapDto(
                symbol,
                string.IsNullOrWhiteSpace(row?.Sector) ? null : row.Sector.Trim(),
                row?.Verdict?.Word,
                figures.Status,
                outside && figures.Status == GapStatus.Overweight,
                figures.CurrentPct,
                figures.TargetPct,
                figures.GapPct,
                figures.CurrentUsd,
                figures.TargetUsd,
                figures.GapUsd,
                NameReason(figures, outside, row)));
        }

        return [.. gaps
            .OrderByDescending(g => Math.Abs(g.GapPct))
            .ThenBy(g => g.Symbol, StringComparer.Ordinal)];
    }

    private static string NameReason(GapFigures figures, bool outside, UniverseRowDto? row)
    {
        if (outside)
        {
            var why = string.IsNullOrWhiteSpace(row?.Reason)
                ? "The target does not hold this name."
                : row.Reason;
            return $"Held outside the target: {Format(figures.CurrentPct)}% of the book. {why}";
        }

        var held = figures.Status == GapStatus.Missing
            ? "Not held."
            : $"Held at {Format(figures.CurrentPct)}%.";
        return $"Target {Format(figures.TargetPct)}%. {held} {row?.Reason}".TrimEnd();
    }

    private static List<SectorGapDto> SectorGaps(List<NameGapDto> names, decimal total, bool hasBook) =>
        [.. names
            .GroupBy(n => n.Sector ?? Unclassified, StringComparer.Ordinal)
            .Select(g =>
            {
                var figures = Figures(g.Sum(n => n.CurrentUsd), g.Sum(n => n.TargetPct), total, hasBook);
                return new SectorGapDto(
                    g.Key, figures.Status, figures.CurrentPct, figures.TargetPct, figures.GapPct, figures.CurrentUsd,
                    figures.TargetUsd, figures.GapUsd, [.. g.Select(n => n.Symbol).Order(StringComparer.Ordinal)]);
            })
            .OrderByDescending(s => Math.Abs(s.GapPct))
            .ThenBy(s => s.Sector, StringComparer.Ordinal)];

    private static List<SleeveGapDto> SleeveGaps(UniverseScreenResult universe, BookFigures book, decimal total, bool hasBook)
    {
        var current = new Dictionary<string, decimal>(StringComparer.Ordinal);
        void Add(string assetClass, decimal usd)
        {
            if (usd > 0m)
            {
                current[assetClass] = current.GetValueOrDefault(assetClass) + usd;
            }
        }

        foreach (var position in book.Positions)
        {
            Add(AssetClassNormalizer.Normalize(position.AssetClass), position.UsdValue);
        }

        Add(AssetClassNormalizer.Cash, book.CashUsd);

        var target = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var sleeve in universe.Construction.Sleeves)
        {
            var key = AssetClassNormalizer.Normalize(sleeve.AssetClass);
            target[key] = target.GetValueOrDefault(key) + sleeve.WeightPct;
        }

        return [.. target.Keys.Union(current.Keys, StringComparer.Ordinal)
            .Select(assetClass =>
            {
                var figures = Figures(current.GetValueOrDefault(assetClass), target.GetValueOrDefault(assetClass), total, hasBook);
                return new SleeveGapDto(
                    assetClass, figures.Status, figures.CurrentPct, figures.TargetPct, figures.GapPct, figures.CurrentUsd,
                    figures.TargetUsd, figures.GapUsd);
            })
            .OrderByDescending(s => Math.Abs(s.GapPct))
            .ThenBy(s => s.AssetClass, StringComparer.Ordinal)];
    }

    /// <summary>
    /// One figure against its target. The gap is computed from the two rounded percentages (and the two rounded
    /// dollar figures), so what is shown always subtracts exactly. A held figure of zero against a target is
    /// Missing; otherwise a gap within <see cref="OnTargetTolerancePct"/> is OnTarget.
    /// </summary>
    private static GapFigures Figures(decimal currentUsd, decimal targetRaw, decimal total, bool hasBook)
    {
        var currentPct = hasBook ? Round(currentUsd / total * Hundred) : 0m;
        var targetPct = Round(targetRaw);
        var gapPct = targetPct - currentPct;
        var currentRounded = Round(currentUsd);
        decimal? targetUsd = hasBook ? Round(targetPct / Hundred * total) : null;
        decimal? gapUsd = targetUsd is { } t ? t - currentRounded : null;

        var status = currentUsd <= 0m && targetPct > 0m
            ? GapStatus.Missing
            : Math.Abs(gapPct) <= OnTargetTolerancePct
                ? GapStatus.OnTarget
                : gapPct > 0m ? GapStatus.Underweight : GapStatus.Overweight;

        return new GapFigures(status, currentPct, targetPct, gapPct, currentRounded, targetUsd, gapUsd);
    }

    private static GapSummaryDto Summarize(List<NameGapDto> names, List<SleeveGapDto> sleeves)
    {
        List<string> Of(string status) => [.. names.Where(n => n.Status == status).Select(n => n.Symbol)];

        var missing = Of(GapStatus.Missing);
        var under = Of(GapStatus.Underweight);
        var over = Of(GapStatus.Overweight);

        var parts = new List<string>();
        if (missing.Count > 0)
        {
            parts.Add($"Missing: {string.Join(", ", missing)}.");
        }

        if (under.Count > 0)
        {
            parts.Add($"Underweight: {string.Join(", ", under)}.");
        }

        if (over.Count > 0)
        {
            parts.Add($"Overweight: {string.Join(", ", over)}.");
        }

        var offTarget = sleeves.Where(s => s.Status != GapStatus.OnTarget).ToList();
        if (offTarget.Count > 0)
        {
            parts.Add("Sleeves: " + string.Join(", ", offTarget.Select(s =>
                s.GapPct > 0m
                    ? $"{s.AssetClass} short by {Format(s.GapPct)} points"
                    : $"{s.AssetClass} over by {Format(-s.GapPct)} points")) + ".");
        }

        return new GapSummaryDto(
            parts.Count == 0 ? "The book matches the constructed target." : string.Join(" ", parts),
            missing,
            under,
            over,
            names.Count(n => n.Status == GapStatus.OnTarget));
    }

    /// <summary>Rounds the way <c>get_allocation_vs_target</c> does (<c>Math.Round(x, 2)</c>), so the current weights agree to the digit (spec 726 SC-005).</summary>
    private static decimal Round(decimal value) => decimal.Round(value, Decimals);

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed record GapFigures(
        string Status, decimal CurrentPct, decimal TargetPct, decimal GapPct, decimal CurrentUsd, decimal? TargetUsd, decimal? GapUsd);
}
