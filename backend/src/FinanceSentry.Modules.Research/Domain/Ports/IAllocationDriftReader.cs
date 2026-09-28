namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (#673): a user's live allocation drift against their IPS, and the users
/// who have a current IPS, for cross-module readers such as Radar's portfolio scanner. Implemented
/// inside Research; the FinanceSentry.Integration adapter reaches Research only through this interface.
/// </summary>
public interface IAllocationDriftReader
{
    /// <summary>The user's per-sleeve drift. <c>HasIps</c> is false (and <c>Sleeves</c> empty) when the user has no current IPS.</summary>
    Task<AllocationDriftReading> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Every user with a current IPS.</summary>
    Task<IReadOnlyList<Guid>> ListUserIdsWithCurrentIpsAsync(CancellationToken ct = default);
}

/// <summary>A user's allocation drift reading.</summary>
public sealed record AllocationDriftReading(
    bool HasIps,
    IReadOnlyList<SleeveDriftReading> Sleeves);

/// <summary>
/// One asset-class sleeve's drift, in percentage points (0-100). Status is Within / OverBand /
/// UnderBand / Unplanned.
/// </summary>
public sealed record SleeveDriftReading(
    string AssetClass,
    decimal TargetPct,
    decimal ActualPct,
    decimal DriftPct,
    string Status);
