namespace FinanceSentry.Core.Utils;

/// <summary>
/// The explicit mapping from an alert type to the policy (Risk rule key) it is derived from (#691).
/// An acknowledgement is recorded against the policy, so every alert class listed here is silenced by
/// it regardless of which module emits the alert. The alert pipeline consults this at emission time;
/// an alert type absent from the map is not policy-derived. A test pins every alert type to either
/// this map or an explicit "not policy-derived" list, so a new emitter cannot fall outside it silently.
/// </summary>
public static class PolicyAlertMap
{
    private static readonly Dictionary<string, string> PolicyByAlertType = new(StringComparer.Ordinal)
    {
        // The liquidity sentinel's projected shortfall is the recurring form of the MinCashBuffer floor.
        ["CashShortfall"] = "MinCashBuffer",
    };

    /// <summary>Alert type → policy key, for the alert classes derived from a policy.</summary>
    public static IReadOnlyDictionary<string, string> Entries => PolicyByAlertType;

    public static bool TryGetPolicyKey(string alertType, out string policyKey)
        => PolicyByAlertType.TryGetValue(alertType, out policyKey!);
}
