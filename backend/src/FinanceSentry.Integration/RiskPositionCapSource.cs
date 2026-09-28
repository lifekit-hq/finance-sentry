namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// 039: implements the Research module's <see cref="IPositionCapSource"/> by reading the Risk rule
/// set - the single home of the single-position cap - through Risk's published
/// <see cref="IRiskLimitsReader"/> port (#673). Lives in the host so neither module references the other.
/// </summary>
public sealed class RiskPositionCapSource(IRiskLimitsReader riskLimits)
    : IPositionCapSource
{
    public async Task<decimal?> GetMaxPositionWeightAsync(Guid userId, CancellationToken ct)
    {
        var limits = await riskLimits.GetCurrentAsync(userId, ct);
        return limits?.MaxPositionWeightPct;
    }
}
