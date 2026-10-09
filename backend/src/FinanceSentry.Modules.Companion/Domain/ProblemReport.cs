namespace FinanceSentry.Modules.Companion.Domain;

using System.Text.Json.Serialization;

/// <summary>What the reporter says went wrong; optional in the request.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProblemReportKind
{
    Broken,
    LooksWrong,
    Idea,
}

/// <summary>The form factor the app reported itself on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProblemReportDevice
{
    Phone,
    Desktop,
}

public enum ProblemReportStatus
{
    /// <summary>Saved, not yet at the fleet inbox (relay not configured, or waiting for a retry).</summary>
    Pending,

    /// <summary>The relay accepted the note.</summary>
    Forwarded,

    /// <summary>Gave up after <see cref="ProblemReportLimits.MaxForwardAttempts"/> failed attempts.</summary>
    Failed,
}

/// <summary>
/// A problem report a signed-in person sent from the app. Everything here is already cleaned
/// (<c>ProblemReportCleaner</c>): the text carries no control characters, long digit runs or amounts, and the
/// page is a route pattern, never a URL. The owner is read from the token, never from the request.
/// </summary>
public sealed class ProblemReport
{
    /// <summary>Also the reference the reporter sees (<c>FS-R-42</c>) and the relay request id (<c>fs-report-42</c>).</summary>
    public long Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Lower-case role at the time of the report: <c>owner</c> or <c>member</c>.</summary>
    public string Role { get; set; } = string.Empty;

    public ProblemReportKind? Kind { get; set; }

    public string? Text { get; set; }

    public string RoutePattern { get; set; } = string.Empty;

    public string AppVersion { get; set; } = string.Empty;

    public ProblemReportDevice Device { get; set; }

    /// <summary>Operating system and browser family, for example <c>iOS Safari</c>.</summary>
    public string Client { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ProblemReportStatus Status { get; set; } = ProblemReportStatus.Pending;

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? ForwardedAt { get; set; }

    public string? LastError { get; set; }
}
