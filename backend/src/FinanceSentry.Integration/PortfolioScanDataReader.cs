namespace FinanceSentry.Integration;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// Feature 043 — implements the Radar module's <see cref="IPortfolioScanDataReader"/> by
/// reading the canonical book (IBookFiguresService), allocation drift (Research's published
/// IAllocationDriftReader port), and risk limits (Risk's published IRiskLimitsReader port, #673).
/// Lives in the Integration layer so Modules.Radar never references Modules.Research or
/// Modules.Risk directly.
/// </summary>
public sealed class PortfolioScanDataReader(
    IBookFiguresService bookFigures,
    IAllocationDriftReader allocationDrift,
    IRiskLimitsReader riskLimits) : IPortfolioScanDataReader
{
    /// <summary>
    /// Risk rule limits are stored as fractions in (0,1] (<see cref="RiskLimits"/>)
    /// despite the <c>Pct</c> suffix, while <see cref="PortfolioScanData"/> is contractually percentage
    /// points (0–100). This adapter is the single place the two units meet, so it converts here.
    /// </summary>
    private const decimal FractionToPercentagePoints = 100m;

    public async Task<IReadOnlyList<Guid>> GetScanUserIdsAsync(CancellationToken ct = default)
    {
        var withRules = await riskLimits.ListUserIdsWithRuleSetsAsync(ct);
        var withIps = await allocationDrift.ListUserIdsWithCurrentIpsAsync(ct);
        return withRules.Union(withIps).Distinct().ToList();
    }

    public async Task<PortfolioScanData?> ReadAsync(Guid userId, CancellationToken ct = default)
    {
        // Read book and drift in parallel; risk limits are a cheap single-row read.
        var bookTask = bookFigures.ReadAsync(userId, ct);
        var driftTask = allocationDrift.GetAsync(userId, ct);
        var rulesTask = riskLimits.GetCurrentAsync(userId, ct);

        await Task.WhenAll(bookTask, driftTask, rulesTask);

        var book = await bookTask;
        var drift = await driftTask;
        var rules = await rulesTask;

        if (book.TotalValueUsd <= 0)
        {
            return null;
        }

        var driftRows = drift.HasIps
            ? drift.Sleeves.Select(s => new ScanSleeveDrift(
                s.AssetClass, s.TargetPct, s.ActualPct, s.DriftPct, s.Status)).ToList()
            : (IReadOnlyList<ScanSleeveDrift>)[];

        var totalUsd = book.TotalValueUsd;
        var topPositions = book.Positions
            .Where(p => p.UsdValue > 0)
            .OrderByDescending(p => p.UsdValue)
            .Select(p => new ScanPosition(
                p.Symbol,
                p.UsdValue,
                totalUsd > 0 ? Math.Round(p.UsdValue / totalUsd * 100m, 2) : 0m))
            .ToList();

        return new PortfolioScanData(
            book.TotalValueUsd,
            book.CashUsd,
            book.IsStale,
            book.StaleSources,
            driftRows,
            topPositions,
            ToPercentagePoints(rules?.MaxPositionWeightPct),
            ToPercentagePoints(rules?.MinCashBufferPct));
    }

    private static decimal? ToPercentagePoints(decimal? fraction)
        => fraction * FractionToPercentagePoints;
}
