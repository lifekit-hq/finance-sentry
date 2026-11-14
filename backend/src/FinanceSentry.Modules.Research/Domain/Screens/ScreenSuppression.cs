namespace FinanceSentry.Modules.Research.Domain.Screens;

/// <summary>
/// The re-nomination guard: a name the user's coverage removed is not nominated again on the evidence that was
/// already in view when it was removed, nor within a cooldown of the removal. Pure.
/// </summary>
public static class ScreenSuppression
{
    /// <summary>
    /// True when a screen hit must not be nominated for a user whose coverage last removed the name at
    /// <paramref name="removedAt"/> (null: never removed, so never suppressed). The hit stands only when its evidence is
    /// later than the removal and the cooldown since the removal has passed. A screen whose data is refreshed every
    /// day (relative strength) has no evidence date of its own; it stamps its run time, so for it the cooldown is the guard.
    /// </summary>
    public static bool Suppressed(DateTimeOffset? removedAt, DateTimeOffset evidenceAt, DateTimeOffset asOf, int cooldownDays)
        => removedAt is { } removed && (evidenceAt <= removed || asOf < removed.AddDays(cooldownDays));
}
