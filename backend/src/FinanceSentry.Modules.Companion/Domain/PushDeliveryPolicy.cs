namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>Bounds for the Web Push sender (spec 859).</summary>
public static class PushDeliveryPolicy
{
    /// <summary>Attempts before a delivery that keeps hitting transient errors is marked Failed.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Events older than this are not pushed: a stale lock-screen alert is noise.</summary>
    public static readonly TimeSpan EventWindow = TimeSpan.FromHours(6);

    /// <summary>How long a push service may hold the message for an offline device.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromHours(24);

    /// <summary>Consecutive 401/403 answers (VAPID not accepted for this subscription) before it is soft-disabled.</summary>
    public const int DisableAfterRejections = 3;

    /// <summary>A soft-disabled subscription is removed once it has stayed disabled this long.</summary>
    public static readonly TimeSpan PruneDisabledAfter = TimeSpan.FromDays(30);

    /// <summary>Delay before the next attempt, indexed by attempts already made (1-based, clamped to the last entry).</summary>
    public static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
    ];

    public static TimeSpan BackoffAfter(int attempts)
        => Backoff[Math.Clamp(attempts - 1, 0, Backoff.Length - 1)];
}
