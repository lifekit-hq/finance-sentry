namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// #700: implements the Risk module's <see cref="IDrawdownPolicySource"/> by reading the owner's
/// recorded drawdown tolerance - intent, held on the IPS - through Research's published
/// <see cref="IRiskToleranceReader"/> port. Translates the IPS whole percent into the fraction the
/// risk layer compares in. Lives in the host so neither module references the other.
/// </summary>
public sealed class IpsDrawdownPolicySource(IRiskToleranceReader riskTolerance) : IDrawdownPolicySource
{
    private const decimal PercentToFraction = 100m;

    public async Task<decimal?> GetMaxDrawdownAsync(Guid userId, CancellationToken ct)
    {
        var tolerance = await riskTolerance.GetCurrentAsync(userId, ct);
        return tolerance?.MaxDrawdownTolerancePct is > 0m and <= PercentToFraction
            ? tolerance.MaxDrawdownTolerancePct / PercentToFraction
            : null;
    }
}
