namespace FinanceSentry.Integration;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// Implements Radar's <see cref="IRiskPictureInputsSource"/> from the canonical book (<see cref="IBookFiguresService"/>),
/// the stored risk rule's position flag (<see cref="IRiskLimitsReader"/>) and the IPS drawdown tolerance
/// (<see cref="IRiskProfileReader"/>, whole percent turned into the fraction the panel compares in). A symbol held on
/// several venues is one position, as every weight rule reads it. Lives in the host so no module references another.
/// </summary>
public sealed class RadarRiskPictureInputsSource(
    IBookFiguresService book,
    IRiskLimitsReader limits,
    IRiskProfileReader profile) : IRiskPictureInputsSource
{
    private const decimal PercentToFraction = 100m;

    public async Task<RiskPictureInputs> ReadAsync(Guid userId, CancellationToken ct = default)
    {
        var figures = await book.ReadAsync(userId, ct);
        var positions = figures.Positions
            .GroupBy(p => (Symbol: p.Symbol.ToUpperInvariant(), p.AssetClass))
            .Select(g => new RiskPosition(
                g.First().Symbol,
                g.Key.AssetClass,
                figures.TotalValueUsd > 0 ? g.Sum(p => p.UsdValue) / figures.TotalValueUsd : 0m))
            .ToList();
        var rule = await limits.GetCurrentAsync(userId, ct);
        var ips = await profile.GetCurrentAsync(userId, ct);
        var tolerance = ips?.MaxDrawdownTolerancePct is > 0m and <= PercentToFraction
            ? ips.MaxDrawdownTolerancePct / PercentToFraction
            : null;
        return new RiskPictureInputs(positions, rule?.MaxPositionWeightPct, tolerance);
    }
}
