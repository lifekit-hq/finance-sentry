namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Core-facing read of the policy acknowledgements Risk owns (#689) — lets the alert pipeline ask
/// "has the operator acknowledged this policy?" without depending on the Risk module (#691).
/// </summary>
public interface IPolicyAckReader
{
    /// <summary>True when the user has an active acknowledgement on <paramref name="policyKey"/> (a Risk rule key).</summary>
    Task<bool> IsPolicyAcknowledgedAsync(Guid userId, string policyKey, CancellationToken ct = default);
}
