namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// Lifecycle/outcome of a captured event (feature 031). Every captured event carries one — none are
/// lost (FR-007). Terminal: <see cref="Delivered"/>, <see cref="SuppressedByMode"/>, <see cref="Failed"/>,
/// <see cref="Expired"/>. Realtime <see cref="DeferredQuietHours"/>/<see cref="SuppressedByRateLimit"/>
/// are re-evaluated next tick.
/// </summary>
public enum EventDisposition
{
    Pending,
    Dispatched,
    HeldForDigest,
    Delivered,
    SuppressedByMode,
    SuppressedByDedup,
    SuppressedByRateLimit,
    DeferredQuietHours,
    Failed,

    /// <summary>
    /// Explicitly dropped without delivery (issue #686 migration): a held-for-digest event that
    /// predated the notification settings backfill and had no scheduled delivery path.
    /// </summary>
    Expired,
}
