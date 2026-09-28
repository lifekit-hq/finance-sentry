namespace FinanceSentry.Modules.Risk.Domain.Ports;

/// <summary>
/// Published read port (#673): the position and cash limits of a user's current risk rule set -
/// the single home of those caps - and the users who have one, for cross-module readers such as
/// Research's opportunity scoring and Radar's portfolio scanner. Implemented inside Risk; the
/// FinanceSentry.Integration adapters reach Risk only through this interface.
/// </summary>
public interface IRiskLimitsReader
{
    /// <summary>The current rule set's limits, or null when the user has no rule set.</summary>
    Task<RiskLimits?> GetCurrentAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Every user with at least one rule set.</summary>
    Task<IReadOnlyList<Guid>> ListUserIdsWithRuleSetsAsync(CancellationToken ct = default);
}

/// <summary>
/// The limits as fractions in (0,1] despite the <c>Pct</c> suffix (the rule set's storage unit);
/// a null limit is unset.
/// </summary>
public sealed record RiskLimits(
    decimal? MaxPositionWeightPct,
    decimal? MinCashBufferPct);
