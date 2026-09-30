namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (#700): the owner's recorded drawdown tolerance on the current IPS - the
/// single home of that intent - for cross-module readers such as the Risk module, which enforces it.
/// Implemented inside Research; the FinanceSentry.Integration adapter reaches Research only through
/// this interface.
/// </summary>
public interface IRiskToleranceReader
{
    /// <summary>The current IPS's risk tolerance, or null when the user has no current IPS.</summary>
    Task<IpsRiskTolerance?> GetCurrentAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// <c>MaxDrawdownTolerancePct</c> is whole percentage points (0-100), as the IPS stores it, and null
/// while the owner has not recorded one.
/// </summary>
public sealed record IpsRiskTolerance(decimal? MaxDrawdownTolerancePct);
