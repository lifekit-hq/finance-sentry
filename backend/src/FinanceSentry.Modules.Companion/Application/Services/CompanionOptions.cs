namespace FinanceSentry.Modules.Companion.Application.Services;

/// <summary>
/// Configuration for the Companion notification policy (feature 031). Bound from the <c>Companion</c>
/// config section. <see cref="AgentTriggerUrl"/> empty ⇒ no realtime push (agent pulls via MCP).
/// </summary>
public sealed class CompanionOptions
{
    public const string SectionName = "Companion";

    /// <summary>Outbound wake URL (the agent runtime's trigger). Empty = pull-only, no realtime push.</summary>
    public string? AgentTriggerUrl { get; set; }

    /// <summary>
    /// Bearer token the wake POST authenticates with. Read from configuration at runtime only — never
    /// logged, never echoed in a payload. Empty = the request is sent without an Authorization header.
    /// </summary>
    public string? AgentTriggerToken { get; set; }

    public string DefaultTimeZoneId { get; set; } = "Europe/Dublin";

    public int? QuietHoursStartLocal { get; set; } = 22;

    public int? QuietHoursEndLocal { get; set; } = 7;

    public int MaxProactivePerHour { get; set; } = 6;

    public int DigestHourLocal { get; set; } = 8;

    /// <summary>Max dispatch attempts before an event is marked Failed.</summary>
    public int MaxDispatchAttempts { get; set; } = 5;

    /// <summary>On first run for a source, look back this many minutes for the initial watermark.</summary>
    public int InitialLookbackMinutes { get; set; } = 15;
}
