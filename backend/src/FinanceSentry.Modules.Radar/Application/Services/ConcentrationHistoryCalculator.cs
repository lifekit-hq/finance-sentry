using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;

namespace FinanceSentry.Modules.Radar.Application.Services;

/// <summary>
/// Turns stored holding captures, snapshot cash and IPS targets into the concentration history. Nothing is stored
/// and nothing is invented: a day with no priced position is skipped, a day with no measured cash split carries no
/// cash share, and fewer than <see cref="MinimumDays"/> days is reported as too little history rather than drawn.
/// </summary>
public static class ConcentrationHistoryCalculator
{
    public const string NotEnoughHistory = "NotEnoughHistory";

    public const int MinimumDays = 2;

    private const int TopCount = 3;

    public static ConcentrationHistory Build(ConcentrationHistoryInputs inputs)
    {
        var cashByDate = inputs.Cash.GroupBy(c => c.Date).ToDictionary(g => g.Key, g => g.Last());

        // One reading per UTC day: the latest capture of that day.
        var points = inputs.Captures
            .GroupBy(c => DateOnly.FromDateTime(c.CapturedAt.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => Point(g.Key, g.OrderBy(c => c.CapturedAt).Last(), cashByDate))
            .OfType<ConcentrationPoint>()
            .ToList();

        var crypto = Target(inputs.Targets, AssetClassNormalizer.Crypto);
        var cash = Target(inputs.Targets, AssetClassNormalizer.Cash);

        return points.Count < MinimumDays
            ? new ConcentrationHistory([], crypto, cash, NotEnoughHistory)
            : new ConcentrationHistory(points, crypto, cash, null);
    }

    private static ConcentrationPoint? Point(
        DateOnly date, ConcentrationCapture capture, Dictionary<DateOnly, ConcentrationCash> cashByDate)
    {
        var priced = capture.Positions
            .Where(p => p.UsdValue > 0m)
            .GroupBy(p => p.Symbol.ToUpperInvariant())
            .Select(g => g.Sum(p => p.UsdValue))
            .OrderByDescending(v => v)
            .ToList();
        var total = capture.Positions.Where(p => p.UsdValue > 0m).Sum(p => p.UsdValue);
        if (total <= 0m)
        {
            return null;
        }

        var crypto = capture.Positions.Where(p => p.IsCrypto && p.UsdValue > 0m).Sum(p => p.UsdValue);
        decimal? cashShare = cashByDate.TryGetValue(date, out var c) && c.Total > 0m ? c.Cash / c.Total : null;
        return new ConcentrationPoint(date, priced.Take(TopCount).Sum() / total, crypto / total, cashShare);
    }

    private static decimal? Target(IReadOnlyList<ConcentrationTarget> targets, string assetClass)
    {
        var matching = targets.Where(t => t.AssetClass == assetClass).ToList();
        return matching.Count == 0 ? null : matching.Sum(t => t.TargetFraction);
    }
}
