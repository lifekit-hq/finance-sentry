namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (spec 726 FR-015): the exclusions of a user's current IPS (tickers or sectors the user ruled
/// out), for cross-module readers such as the Portfolio Construction module. Implemented inside Research over its
/// own IPS query; the FinanceSentry.Integration adapter reaches Research only through this interface.
/// </summary>
public interface IIpsExclusionsReader
{
    /// <summary>The current IPS's exclusions, or an empty list when the user has no current IPS.</summary>
    Task<IReadOnlyList<string>> GetCurrentAsync(Guid userId, CancellationToken ct = default);
}
