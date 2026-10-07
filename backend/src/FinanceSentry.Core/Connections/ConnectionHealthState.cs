namespace FinanceSentry.Core.Connections;

/// <summary>
/// Where a provider connection stands, by the connection-health policy (Option B, report §7). Stored as
/// its name, so the values can be reordered but never renamed without a migration.
/// </summary>
public enum ConnectionHealthState
{
    /// <summary>The last attempt succeeded.</summary>
    Healthy,

    /// <summary>Failing, but not yet long enough to tell the user; silent.</summary>
    Degraded,

    /// <summary>Failed often and long enough that the user should be told; nothing for them to do.</summary>
    Failing,

    /// <summary>The credential is gone; only the user can fix it by reconnecting. Holds until a success.</summary>
    ActionRequired,

    /// <summary>Failed continuously for long enough that the connection is presumed closed.</summary>
    Dormant,
}
