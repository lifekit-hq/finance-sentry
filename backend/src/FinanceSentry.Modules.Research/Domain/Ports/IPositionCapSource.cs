namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Read-only cross-module port (feature 039). Opportunity scoring reads the maximum single-position
/// cap from its single home — the Risk rule set (Risk module) — not a copy on the IPS. The concrete
/// adapter lives in the composition root (FinanceSentry.API) so the Research and Risk modules never
/// reference each other.
/// </summary>
public interface IPositionCapSource
{
    /// <summary>The current user's max single-position cap as a fraction in (0,1], or null if unset.</summary>
    Task<decimal?> GetMaxPositionWeightAsync(Guid userId, CancellationToken ct);

    /// <summary>Every user with a risk rule set, for the recommendation rule sweep (#1069). Job-safe.</summary>
    Task<IReadOnlyList<Guid>> ListUserIdsAsync(CancellationToken ct);
}
