namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Cross-module read contract (#698): when a detector's alert type last fired for a user, so a
/// monitor outside the Alerts module can tell a quiet detector from a broken one. Implemented by the
/// Alerts module.
/// </summary>
public interface IAlertFireHistoryReader
{
    /// <summary>
    /// When the newest alert of <paramref name="alertType"/> was raised for the user, whatever its
    /// state now (open, read, resolved or dismissed), or null when none is on record. Resolved and
    /// dismissed alerts are purged after 90 days, so history reaches back at most that far.
    /// </summary>
    Task<DateTimeOffset?> GetLastRaisedAtAsync(Guid userId, string alertType, CancellationToken ct = default);
}
