namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Core-facing read of the policy acknowledgements Risk owns (#689) — lets the alert pipeline ask
/// "is this policy currently silenced by an acknowledgement?" without depending on the Risk module (#691).
/// </summary>
public interface IPolicyAckReader
{
    /// <summary>
    /// True when the user has an active acknowledgement on <paramref name="policyKey"/> (a Risk rule key)
    /// that has not worsened past its step — the same re-open rule Risk applies to its own violations.
    /// </summary>
    Task<bool> IsPolicySilencedAsync(Guid userId, string policyKey, CancellationToken ct = default);
}
