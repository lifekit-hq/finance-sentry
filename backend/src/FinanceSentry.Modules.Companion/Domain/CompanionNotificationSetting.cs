namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// A user's proactivity dial + guardrails (feature 031). One row per user; created lazily on first
/// set/read. Governs proactive outreach only.
/// </summary>
public sealed class CompanionNotificationSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public NotificationMode Mode { get; set; } = NotificationMode.Scan;

    /// <summary>Quiet-hours start hour (0–23) in <see cref="TimeZoneId"/>; null = no quiet hours.</summary>
    public int? QuietHoursStartLocal { get; set; }

    public int? QuietHoursEndLocal { get; set; }

    /// <summary>IANA tz for quiet-hours + digest timing; null = use configured default.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>Rate-limit cap on proactive outreach per rolling hour.</summary>
    public int MaxProactivePerHour { get; set; }

    /// <summary>Local hour (0–23) the daily digest is produced.</summary>
    public int DigestHourLocal { get; set; }

    /// <summary>Web Push opt-in (spec 859). Independent of <see cref="Mode"/>; off until the user turns it on.</summary>
    public bool PushEnabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
