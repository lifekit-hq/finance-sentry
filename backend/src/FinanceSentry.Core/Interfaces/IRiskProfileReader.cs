namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Published read port (#454 S7): the risk fields on the person's current Investment Policy Statement, for
/// cross-module readers such as Wealth, which sets them beside the capacity indicator. Implemented inside Research, so
/// Wealth never references it. Callers run with the person in scope.
/// </summary>
public interface IRiskProfileReader
{
    /// <summary>The current IPS's risk fields, or null when the person has no current IPS.</summary>
    Task<IpsRiskProfile?> GetCurrentAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// What the IPS records about risk: behavioural <paramref name="RiskTolerance"/> and <paramref name="RiskCapacity"/>
/// (1-5, capacity null until it is set), the largest drawdown the owner says they would sit through, when these were last
/// measured and whether a re-measurement is due, plus the horizon and emergency cushion the statement states.
/// </summary>
public sealed record IpsRiskProfile(
    int RiskTolerance,
    int? RiskCapacity,
    decimal? MaxDrawdownTolerancePct,
    DateTimeOffset? RiskMeasuredAt,
    bool RemeasurementDue,
    int PrimaryHorizonYears,
    decimal? EmergencyCushionUsd);
