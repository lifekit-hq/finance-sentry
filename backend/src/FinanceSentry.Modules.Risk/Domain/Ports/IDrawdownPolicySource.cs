namespace FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// Read-only cross-module port (#700). The drawdown the owner says they can sit through is intent, so
/// it is held in its single home - the Investment Policy Statement (Research module) - and the Risk
/// module enforces it. The concrete adapter lives in the composition root (FinanceSentry.Integration)
/// so the Risk and Research modules never reference each other.
/// </summary>
public interface IDrawdownPolicySource
{
    /// <summary>
    /// The tolerated peak-to-trough decline as a fraction in (0,1] - the unit the risk layer compares in -
    /// or null when the owner has recorded none (nothing to enforce).
    /// </summary>
    Task<decimal?> GetMaxDrawdownAsync(Guid userId, CancellationToken ct);
}
