namespace FinanceSentry.Core.Connections;

/// <summary>
/// Thresholds of the connection-health policy, bound from the <c>ConnectionHealth</c> config section.
/// The defaults are the captain's decision of 2026-10-06 (report §13): the user hears of a non-credential
/// failure after at least 3 failures and 2 h without success, and a connection failing for 7 days goes Dormant.
/// </summary>
public sealed class ConnectionHealthOptions
{
    public const string SectionName = "ConnectionHealth";

    /// <summary>Consecutive failures before a connection can become Failing.</summary>
    public int FailingMinConsecutiveFailures { get; set; } = 3;

    /// <summary>Time without a success before a connection can become Failing.</summary>
    public TimeSpan FailingMinTimeWithoutSuccess { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Continuous failure after which a connection becomes Dormant.</summary>
    public TimeSpan DormantAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>A suspect credential failure is confirmed when seen again at least this long after the first.</summary>
    public TimeSpan SuspectConfirmationWindow { get; set; } = TimeSpan.FromMinutes(30);
}
