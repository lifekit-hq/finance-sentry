namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (#673): the allocation section of a user's current IPS - the single home
/// of target allocation - for cross-module readers such as the Risk module. Implemented inside
/// Research; the FinanceSentry.Integration adapter reaches Research only through this interface.
/// </summary>
public interface IIpsAllocationReader
{
    /// <summary>The current IPS's allocation targets and rebalancing band, or null when the user has no current IPS.</summary>
    Task<IpsAllocationPolicy?> GetCurrentAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// The IPS allocation targets plus its rebalancing rule's band. Every value is whole percentage
/// points (0-100), as the IPS stores them.
/// </summary>
public sealed record IpsAllocationPolicy(
    IReadOnlyList<IpsAllocationSleeve> Sleeves,
    decimal AbsoluteBandPct,
    decimal RelativeBandPct);

/// <summary>One IPS asset-class sleeve. <c>MaxPct</c> is 0 when the sleeve carries no explicit band.</summary>
public sealed record IpsAllocationSleeve(
    string AssetClass,
    decimal TargetPct,
    decimal MinPct,
    decimal MaxPct);
