namespace FinanceSentry.Integration;

using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Wealth.Domain.Ports;

/// <summary>
/// Implements Radar's <see cref="IConcentrationHistorySource"/> from Risk's <see cref="IBookHoldingsHistoryReader"/>,
/// Wealth's <see cref="IBookCashHistoryReader"/> and Research's <see cref="IIpsAllocationReader"/> (whole percent
/// turned into fractions, labels normalised to the canonical classes). Lives in the host so no module references another.
/// </summary>
public sealed class RadarConcentrationHistorySource(
    IBookHoldingsHistoryReader holdings,
    IBookCashHistoryReader cash,
    IIpsAllocationReader ips) : IConcentrationHistorySource
{
    private const decimal PercentToFraction = 100m;

    public async Task<ConcentrationHistoryInputs> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var captures = await holdings.ListSinceAsync(userId, since, ct);
        var days = await cash.GetDailyAsync(userId, from, DateOnly.FromDateTime(DateTime.UtcNow), ct);
        var policy = await ips.GetCurrentAsync(userId, ct);

        return new ConcentrationHistoryInputs(
            [.. captures.Select(c => new ConcentrationCapture(
                c.CapturedAt,
                [.. c.Positions.Select(p => new ConcentrationPosition(
                    p.Symbol, string.Equals(p.Sleeve, RiskSleeve.Crypto, StringComparison.OrdinalIgnoreCase), p.UsdValue))]))],
            [.. days.Select(d => new ConcentrationCash(d.Date, d.CashUsd, d.TotalUsd))],
            [.. (policy?.Sleeves ?? []).Select(s => new ConcentrationTarget(
                AssetClassNormalizer.Normalize(s.AssetClass), s.TargetPct / PercentToFraction))]);
    }
}
